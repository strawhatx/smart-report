using System.Runtime.CompilerServices;
using SmartReport.Core.Abstractions;
using SmartReport.Core.Policy;
using SmartReport.Core.Sql;
using SmartReport.Core.Specs;

namespace SmartReport.Core.Reporting;

/// <summary>
/// Question in, report out — plan, validate, authorize, compile, execute.
///
/// <para>
/// The order matters and is the product: nothing is compiled before it has been validated against
/// the catalog and authorized for the principal, and nothing is executed on a connection that has
/// not had that principal applied to it.
/// </para>
/// </summary>
public sealed class ReportPipeline(
    IReportPlanner planner,
    SpecValidator validator,
    PolicyEngine policy,
    SqlCompiler compiler,
    IReportQueryRunner runner,
    IReportNarrator? narrator = null)
{
    public IAsyncEnumerable<ReportEvent> RunAsync(
        string question, Principal principal, CancellationToken cancellationToken) =>
        RunAsync(new PlanRequest(question), principal, cancellationToken);

    public async IAsyncEnumerable<ReportEvent> RunAsync(
        PlanRequest request,
        Principal principal,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var question = request.Question;
        ReportSpec? planned = null;
        string? failure = null;

        await foreach (var step in Plan(request, principal, cancellationToken))
        {
            switch (step)
            {
                case PlannerEvent.Thinking thinking:
                    yield return new ReportEvent.Thinking(thinking.Text);
                    break;

                case PlannerEvent.NoMatch noMatch:
                    // Nothing in the catalog fits. A first-class outcome, not a failure — the
                    // honest answer to a question the product cannot yet answer.
                    yield return new ReportEvent.Refused(noMatch.Message);
                    yield return new ReportEvent.Done();
                    yield break;

                case PlannerEvent.Planned result:
                    // Held rather than acted on: the planner may still emit narration after it,
                    // and nothing happens to the plan until the stream is exhausted.
                    planned = result.Spec;
                    break;
            }
        }

        // Neither a plan nor a refusal. A provider returning something unusable, which is a
        // fault rather than an answer — reported as Failed so it is not mistaken for a decision.
        if (planned is null)
        {
            yield return new ReportEvent.Failed("The planner didn't produce a plan.");
            yield return new ReportEvent.Done();
            yield break;
        }

        var validated = validator.Validate(planned);

        // One correction attempt. The catalog is not uniform — the same idea is called
        // `shipped_at` on one model and `received_at` on another — and the schema cannot express
        // "this key, but only on that model". Refusing a reasonable question over a naming
        // detail is a worse answer than asking the planner to try again with the errors in hand.
        if (!validated.IsValid && planner.CanRepair && !request.IsRetry)
        {
            yield return new ReportEvent.Thinking(
                $"That plan didn't fit the catalog ({validated.Summary}) — asking it to correct.");

            ReportSpec? corrected = null;
            var retry = request with { ValidationProblems = validated.Summary };

            await foreach (var step in Plan(retry, principal, cancellationToken))
            {
                switch (step)
                {
                    case PlannerEvent.Planned result:
                        corrected = result.Spec;
                        break;
                    case PlannerEvent.NoMatch noMatch:
                        yield return new ReportEvent.Refused(noMatch.Message);
                        yield return new ReportEvent.Done();
                        yield break;
                }
            }

            if (corrected is not null)
            {
                validated = validator.Validate(corrected);
            }
        }

        if (!validated.IsValid)
        {
            // A plan that still fails validation is a bad answer, not an attack, and the user
            // gets a straight explanation of what wasn't available.
            yield return new ReportEvent.Refused(validated.Summary);
            yield return new ReportEvent.Done();
            yield break;
        }

        yield return new ReportEvent.Planned(validated.Spec!);

        // Authorization happens before a single query is compiled, so a measure this person may
        // not use never becomes SQL at all — the difference between "returned no rows" and
        // "was never asked".
        var governed = policy.Govern(validated.Spec!, principal);

        // Every decision, including the allowed ones. The UI chooses what to show; the stream
        // carries the whole record so it can be read in full.
        foreach (var decision in governed.Decisions)
        {
            yield return new ReportEvent.Guard(decision);
        }

        if (governed.IsRefused)
        {
            var denial = governed.Decisions.FirstOrDefault(d => d.Outcome == PolicyOutcome.Denied);
            yield return new ReportEvent.Refused(governed.RefusalReason, denial?.Remedy);
            yield return new ReportEvent.Done();
            yield break;
        }

        var executed = new List<(CompiledQuery, QueryResult)>();

        // Compiled and run one at a time, in order, so the rail fills as the work happens rather
        // than after it. The order is also what ReportBuilder relies on to pair each result with
        // the governed query it came from.
        foreach (var query in governed.Queries)
        {
            var compiled = compiler.Compile(query);

            // Shown before it runs. The query is inspectable by design — the whole claim is that
            // nothing in it came from the question.
            yield return new ReportEvent.Sql(compiled.Model, compiled.Sql);

            QueryResult? result = null;
            try
            {
                result = await runner.RunAsync(compiled, principal, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Deliberately not logged here: core has no logger, and a Failed event carries
                // everything the caller needs to log it with the context it already has (C5).
                failure = "The query couldn't be run. Nothing was changed.";
            }

            // Stop at the first failure rather than running the rest: a partial report whose
            // missing panel is unexplained is worse than a clear error.
            if (failure is not null)
            {
                break;
            }

            yield return new ReportEvent.Rows(compiled.Model, result!.Rows.Count);
            executed.Add((compiled, result));
        }

        if (failure is not null)
        {
            yield return new ReportEvent.Failed(failure);
            yield return new ReportEvent.Done();
            yield break;
        }

        var document = ReportBuilder.Build(governed, executed, planner.ProviderName);
        yield return new ReportEvent.Document(document);

        // Narration runs last and cannot fail the report. By this point every query has already
        // been authorized and executed, so the narrator is describing output rather than asking
        // for anything.
        if (narrator is { IsEnabled: true })
        {
            IReadOnlyList<Finding> findings = [];
            try
            {
                findings = await narrator.DescribeAsync(question, document, principal, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Deliberately swallowed: prose about a report is not worth losing the report
                // over. The absence is visible — no findings appear — so nothing is being hidden.
            }

            if (findings.Count > 0)
            {
                yield return new ReportEvent.Findings(findings, narrator.Disclosure);
            }
        }

        yield return new ReportEvent.Done();
    }

    /// <summary>
    /// Wraps planning so an unreachable local model becomes one clear message rather than an
    /// unhandled exception mid-stream. Note there is no fallback to another planner.
    /// </summary>
    private async IAsyncEnumerable<PlannerEvent> Plan(
        PlanRequest request, Principal principal, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerator = planner.PlanAsync(request, principal, cancellationToken).GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                PlannerEvent? current = null;
                string? unreachable = null;

                try
                {
                    if (await enumerator.MoveNextAsync())
                    {
                        current = enumerator.Current;
                    }
                }
                catch (InvalidOperationException ex)
                {
                    unreachable = ex.Message;
                }

                if (unreachable is not null)
                {
                    yield return new PlannerEvent.NoMatch(unreachable);
                    break;
                }

                if (current is null)
                {
                    break;
                }

                yield return current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }
}

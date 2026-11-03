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
    IReportQueryRunner runner)
{
    public async IAsyncEnumerable<ReportEvent> RunAsync(
        string question,
        Principal principal,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ReportSpec? planned = null;
        string? failure = null;

        await foreach (var step in Plan(question, principal, cancellationToken))
        {
            switch (step)
            {
                case PlannerEvent.Thinking thinking:
                    yield return new ReportEvent.Thinking(thinking.Text);
                    break;

                case PlannerEvent.NoMatch noMatch:
                    yield return new ReportEvent.Refused(noMatch.Message);
                    yield return new ReportEvent.Done();
                    yield break;

                case PlannerEvent.Planned result:
                    planned = result.Spec;
                    break;
            }
        }

        if (planned is null)
        {
            yield return new ReportEvent.Failed("The planner didn't produce a plan.");
            yield return new ReportEvent.Done();
            yield break;
        }

        var validated = validator.Validate(planned);
        if (!validated.IsValid)
        {
            // A plan that fails validation is a bad answer, not an attack, and the user gets a
            // straight explanation of what wasn't available.
            yield return new ReportEvent.Refused(validated.Summary);
            yield return new ReportEvent.Done();
            yield break;
        }

        yield return new ReportEvent.Planned(validated.Spec!);

        var governed = policy.Govern(validated.Spec!, principal);

        foreach (var decision in governed.Decisions)
        {
            yield return new ReportEvent.Guard(decision);
        }

        if (governed.IsRefused)
        {
            yield return new ReportEvent.Refused(governed.RefusalReason);
            yield return new ReportEvent.Done();
            yield break;
        }

        var executed = new List<(CompiledQuery, QueryResult)>();

        foreach (var query in governed.Queries)
        {
            var compiled = compiler.Compile(query);
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

        yield return new ReportEvent.Document(
            ReportBuilder.Build(governed, executed, planner.ProviderName));

        yield return new ReportEvent.Done();
    }

    /// <summary>
    /// Wraps planning so an unreachable local model becomes one clear message rather than an
    /// unhandled exception mid-stream. Note there is no fallback to another planner.
    /// </summary>
    private async IAsyncEnumerable<PlannerEvent> Plan(
        string question, Principal principal, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerator = planner.PlanAsync(question, principal, cancellationToken).GetAsyncEnumerator(cancellationToken);
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

using SmartReport.Core.Abstractions;
using SmartReport.Core.Reporting;
using SmartReport.Core.Catalog;
using SmartReport.Core.Specs;
using Xunit;

namespace SmartReport.Tests;

/// <summary>
/// The reported bug: a filter that exists on one model, used on another.
///
/// <para>
/// The JSON schema offers the union of every filter name across the catalog, because a
/// per-model schema needs conditional subschemas that providers handle inconsistently. So
/// <c>period_month</c> — real, on <c>billing</c> — can be aimed at <c>returns</c>, which calls
/// its date <c>received_at</c>. The validator was right to reject it; the user was right that
/// being refused over a naming detail is a bad answer.
/// </para>
/// </summary>
public class PlanRepairTests
{
    private readonly SpecValidator _validator = new(TestPrincipals.Catalog);

    [Fact]
    public void The_reported_case_is_still_rejected_when_it_is_genuinely_wrong()
    {
        var result = _validator.Validate(new ReportSpec("Returns", [
            new SpecQuery("returns", ["return_count"], ["reason"],
                [new SpecFilter("period_month", FilterOp.Between, ["2026-07-01", "2026-10-01"])], null),
        ]));

        Assert.False(result.IsValid);
        Assert.Contains("period_month", result.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("shipping")]
    [InlineData("orders")]
    [InlineData("returns")]
    [InlineData("billing")]
    [InlineData("benchmarks")]
    public void Every_model_with_a_date_accepts_the_same_period_filter(string model)
    {
        // One name that works everywhere, so the mistake above stops being available.
        var metric = TestPrincipals.Catalog.Model(model)!.Metrics.Keys.First();

        var result = _validator.Validate(new ReportSpec("T", [
            new SpecQuery(model, [metric], [],
                [new SpecFilter(DataCatalog.PeriodFilter, FilterOp.Between, ["2026-07-01", "2026-10-01"])], null),
        ]));

        Assert.True(result.IsValid, result.Summary);
    }

    [Fact]
    public void Inventory_has_no_period_because_it_has_no_date()
    {
        // The alias is only added where there is a date column to point it at — offering it on
        // a model with no time axis would be a different lie.
        Assert.Null(TestPrincipals.Catalog.Model("inventory")!.Filter(DataCatalog.PeriodFilter));
    }

    [Fact]
    public void The_period_alias_compiles_to_each_model_s_own_date_column()
    {
        var shipping = TestPrincipals.Catalog.Model("shipping")!.Filter(DataCatalog.PeriodFilter)!;
        var returns = TestPrincipals.Catalog.Model("returns")!.Filter(DataCatalog.PeriodFilter)!;

        Assert.Equal("s.shipped_at", shipping.Expression);
        Assert.Equal("r.received_at", returns.Expression);
    }

    [Fact]
    public void A_retry_is_marked_so_the_pipeline_cannot_loop()
    {
        Assert.False(new PlanRequest("q").IsRetry);
        Assert.True(new PlanRequest("q", ValidationProblems: "something was wrong").IsRetry);
    }
}

/// <summary>
/// The repair loop itself: one correction attempt, the errors handed back, and no chance of
/// looping if the second attempt is wrong too.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PlanRepairLoopTests(PostgresFixture fixture)
{
    /// <summary>Gets it wrong first — the reported bug — then right once told what was wrong.</summary>
    private sealed class TwoAttemptPlanner : IReportPlanner
    {
        public string ProviderName => "stub";
        public bool CanRepair => true;
        public int Attempts { get; private set; }
        public string? ProblemsSeen { get; private set; }

        public async IAsyncEnumerable<PlannerEvent> PlanAsync(
            PlanRequest request, SmartReport.Core.Policy.Principal principal,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Attempts++;
            await Task.CompletedTask;

            if (!request.IsRetry)
            {
                // period_month is real on billing, and wrong here.
                yield return new PlannerEvent.Planned(new ReportSpec("Returns", [
                    new SpecQuery("returns", ["return_count"], ["reason"],
                        [new SpecFilter("period_month", FilterOp.Between, ["2026-07-01", "2026-10-01"])], null),
                ]));
                yield break;
            }

            ProblemsSeen = request.ValidationProblems;
            yield return new PlannerEvent.Planned(new ReportSpec("Returns", [
                new SpecQuery("returns", ["return_count"], ["reason"],
                    [new SpecFilter("period", FilterOp.Between, ["2026-07-01", "2026-10-01"])], null),
            ]));
        }
    }

    /// <summary>Wrong every time, to prove the loop stops.</summary>
    private sealed class AlwaysWrongPlanner : IReportPlanner
    {
        public string ProviderName => "stub";
        public bool CanRepair => true;
        public int Attempts { get; private set; }

        public async IAsyncEnumerable<PlannerEvent> PlanAsync(
            PlanRequest request, SmartReport.Core.Policy.Principal principal,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Attempts++;
            await Task.CompletedTask;
            yield return new PlannerEvent.Planned(new ReportSpec("x", [
                new SpecQuery("returns", ["return_count"], [],
                    [new SpecFilter("period_month", FilterOp.Eq, ["2026-10-01"])], null),
            ]));
        }
    }

    private async Task<List<ReportEvent>> RunAsync(IReportPlanner planner)
    {
        var catalog = DataCatalog.CreateDefault();
        var pipeline = new SmartReport.Core.Reporting.ReportPipeline(
            planner,
            new SpecValidator(catalog),
            new SmartReport.Core.Policy.PolicyEngine(catalog),
            new SmartReport.Core.Sql.SqlCompiler(catalog),
            new SmartReport.Data.ReportQueryRunner(fixture.AppDatabase));

        var events = new List<ReportEvent>();
        await foreach (var e in pipeline.RunAsync("returns by reason this quarter", TestPrincipals.Dana, CancellationToken.None))
        {
            events.Add(e);
        }
        return events;
    }

    [Fact]
    public async Task A_wrong_filter_is_corrected_rather_than_refused()
    {
        var planner = new TwoAttemptPlanner();
        var events = await RunAsync(planner);

        Assert.Equal(2, planner.Attempts);
        Assert.Contains("period_month", planner.ProblemsSeen!, StringComparison.Ordinal);

        Assert.Empty(events.OfType<ReportEvent.Refused>());
        Assert.Single(events.OfType<ReportEvent.Document>());
    }

    [Fact]
    public async Task A_plan_that_stays_wrong_is_refused_after_exactly_one_retry()
    {
        var planner = new AlwaysWrongPlanner();
        var events = await RunAsync(planner);

        Assert.Equal(2, planner.Attempts);
        Assert.Single(events.OfType<ReportEvent.Refused>());
        Assert.Empty(events.OfType<ReportEvent.Sql>());
    }
}

/// <summary>
/// A "vs last month" delta is only meaningful if the headline covers a comparable period. The
/// planner can produce a September comparison beside an all-time headline, which reports a
/// 1,363% improvement in large type.
/// </summary>
public class ComparisonCoherenceTests
{
    private readonly SpecValidator _validator = new(TestPrincipals.Catalog);

    private static SpecQuery Headline(bool scoped) => new(
        "shipping", ["orders_shipped"], [],
        scoped ? [new SpecFilter("period", FilterOp.Between, ["2026-10-01", "2026-11-01"])] : [],
        null, "headline");

    private static SpecQuery Comparison() => new(
        "shipping", ["orders_shipped"], [],
        [new SpecFilter("period", FilterOp.Between, ["2026-09-01", "2026-10-01"])],
        null, "comparison:September");

    [Fact]
    public void A_comparison_beside_an_unscoped_headline_is_dropped()
    {
        var result = _validator.Validate(new ReportSpec("T", [Headline(scoped: false), Comparison()]));

        Assert.True(result.IsValid);
        Assert.DoesNotContain(result.Spec!.Queries,
            q => q.Label?.StartsWith("comparison") == true);
    }

    [Fact]
    public void A_comparison_beside_a_scoped_headline_is_kept()
    {
        var result = _validator.Validate(new ReportSpec("T", [Headline(scoped: true), Comparison()]));

        Assert.True(result.IsValid);
        Assert.Contains(result.Spec!.Queries, q => q.Label?.StartsWith("comparison") == true);
    }

    [Fact]
    public void Dropping_the_comparison_leaves_the_rest_of_the_report_intact()
    {
        var breakdown = new SpecQuery("shipping", ["late_shipments"], ["carrier"], [], null, "");

        var result = _validator.Validate(
            new ReportSpec("T", [Headline(scoped: false), Comparison(), breakdown]));

        Assert.Equal(2, result.Spec!.Queries.Count);
        Assert.Contains(result.Spec.Queries, q => q.Dimensions.Contains("carrier"));
    }
}

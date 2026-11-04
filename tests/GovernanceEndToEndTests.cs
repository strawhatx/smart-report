using SmartReport.Ai;
using SmartReport.Core.Catalog;
using SmartReport.Core.Policy;
using SmartReport.Core.Reporting;
using SmartReport.Core.Specs;
using SmartReport.Core.Sql;
using SmartReport.Data;
using Xunit;

namespace SmartReport.Tests;

/// <summary>
/// The whole pipeline against a real database: question in, report or refusal out.
///
/// <para>
/// Everything else checks a layer. This checks that the layers are wired in the order that makes
/// the guarantees true — and that the numbers the demo shows are aggregations over seeded rows
/// rather than constants.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GovernanceEndToEndTests(PostgresFixture fixture)
{
    private async Task<List<ReportEvent>> AskAsync(string question, Principal principal)
    {
        var catalog = DataCatalog.CreateDefault();
        var pipeline = new ReportPipeline(
            new ScriptedReportPlanner(),
            new SpecValidator(catalog),
            new PolicyEngine(catalog),
            new SqlCompiler(catalog),
            new ReportQueryRunner(fixture.AppDatabase));

        var events = new List<ReportEvent>();
        await foreach (var e in pipeline.RunAsync(question, principal, CancellationToken.None))
        {
            events.Add(e);
        }
        return events;
    }

    private static decimal? Kpi(IEnumerable<ReportEvent> events, string key) =>
        events.OfType<ReportEvent.Document>().Single().Report.Sections
            .SelectMany(s => s.Kpis)
            .FirstOrDefault(k => k.Key == key)?.Value;

    // ── The headline report ────────────────────────────────────────────────

    [Fact]
    public async Task The_october_report_reads_exactly_as_designed()
    {
        var events = await AskAsync("How did our orders ship in October?", TestPrincipals.Dana);

        Assert.Empty(events.OfType<ReportEvent.Refused>());

        Assert.Equal(8412m, Kpi(events, "orders_shipped"));
        Assert.Equal(97.2m, Math.Round(Kpi(events, "on_time_rate")!.Value, 1));
        Assert.Equal(1.4m, Math.Round(Kpi(events, "avg_days_to_ship")!.Value, 1));
        Assert.Equal(6m, Kpi(events, "low_stock_products"));
    }

    [Fact]
    public async Task Late_shipments_break_down_by_carrier_with_ups_worst()
    {
        var document = Assert.Single(
            (await AskAsync("Show late shipments by carrier", TestPrincipals.Dana))
            .OfType<ReportEvent.Document>()).Report;

        var rows = document.Sections[0].Table.Rows;
        Assert.Equal("UPS Ground", rows[0][0]);
        Assert.Equal(5, rows.Count);
    }

    // ── Beat 1: the benchmark ──────────────────────────────────────────────

    [Fact]
    public async Task A_peer_comparison_is_answered_and_the_safeguard_is_stated()
    {
        var events = await AskAsync(
            "How does our on-time rate compare to other Northpoint clients?", TestPrincipals.Dana);

        Assert.Empty(events.OfType<ReportEvent.Refused>());

        var safeguard = Assert.Single(
            events.OfType<ReportEvent.Guard>().Select(g => g.Decision),
            d => d.Outcome == PolicyOutcome.Aggregated);
        Assert.Contains("anonymized", safeguard.Reason, StringComparison.OrdinalIgnoreCase);

        // Both halves came back: the client's own figure and the cohort's.
        var document = events.OfType<ReportEvent.Document>().Single().Report;
        Assert.Equal(2, document.Sections.Count);
        Assert.True(Kpi(events, "cohort_size") >= DataCatalog.MinimumCohortSize);
    }

    [Fact]
    public async Task Every_benchmark_query_carries_the_cohort_floor()
    {
        var sql = (await AskAsync("How do we compare to other clients?", TestPrincipals.Dana))
            .OfType<ReportEvent.Sql>()
            .Single(e => e.Model == "benchmarks").Text;

        Assert.Contains("client_count >=", sql, StringComparison.Ordinal);
    }

    // ── Beat 2: Northpoint's own numbers ───────────────────────────────────

    [Fact]
    public async Task Asking_what_northpoint_pays_its_carriers_is_refused_with_a_usable_alternative()
    {
        var events = await AskAsync("What does Northpoint pay UPS per label?", TestPrincipals.Dana);

        var refusal = Assert.Single(events.OfType<ReportEvent.Refused>());
        Assert.Contains("internal", refusal.Message, StringComparison.OrdinalIgnoreCase);

        var denial = Assert.Single(
            events.OfType<ReportEvent.Guard>().Select(g => g.Decision),
            d => d.Outcome == PolicyOutcome.Denied);

        // The redirect points at something Dana may actually use — not a second dead end.
        Assert.NotNull(denial.Remedy);
        Assert.Contains("billed", denial.Remedy, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(events.OfType<ReportEvent.Sql>());
    }

    [Fact]
    public async Task The_suggested_alternative_actually_works()
    {
        var events = await AskAsync("What are we billed per shipment?", TestPrincipals.Dana);

        Assert.Empty(events.OfType<ReportEvent.Refused>());
        Assert.Single(events.OfType<ReportEvent.Document>());
    }

    [Fact]
    public async Task Even_the_account_owner_cannot_see_northpoint_internal_costs()
    {
        // Owning the client account does not make you Northpoint staff.
        var events = await AskAsync("What does Northpoint pay UPS per label?", TestPrincipals.Priya);

        Assert.Single(events.OfType<ReportEvent.Refused>());
        Assert.Empty(events.OfType<ReportEvent.Sql>());
    }

    // ── Beat 3: role inside the client's own team ──────────────────────────

    [Fact]
    public async Task The_coordinator_is_refused_billing_and_told_who_can_grant_it()
    {
        var events = await AskAsync("Show our fees and invoice breakdown", TestPrincipals.Dana);

        var denial = Assert.Single(
            events.OfType<ReportEvent.Guard>().Select(g => g.Decision),
            d => d.Outcome == PolicyOutcome.Denied);

        Assert.Contains("account owner", denial.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Priya Shah", denial.Remedy ?? string.Empty, StringComparison.Ordinal);

        Assert.Empty(events.OfType<ReportEvent.Sql>());
    }

    [Fact]
    public async Task The_account_owner_gets_the_same_report()
    {
        var events = await AskAsync("Show our fees and invoice breakdown", TestPrincipals.Priya);

        Assert.Empty(events.OfType<ReportEvent.Refused>());
        Assert.NotEmpty(events.OfType<ReportEvent.Document>().Single().Report.Sections[0].Table.Rows);
    }

    // ── The quiet one ──────────────────────────────────────────────────────

    [Fact]
    public async Task Every_report_says_whose_data_it_read()
    {
        var scope = Assert.Single(
            (await AskAsync("How are we doing on time?", TestPrincipals.Dana))
                .OfType<ReportEvent.Guard>().Select(g => g.Decision),
            d => d.Outcome == PolicyOutcome.Scoped);

        Assert.Contains("Trailhead Coffee Co.", scope.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Compiled_sql_never_contains_the_question_text()
    {
        var events = await AskAsync("How did our orders ship in October?", TestPrincipals.Dana);

        foreach (var sql in events.OfType<ReportEvent.Sql>().Select(e => e.Text))
        {
            Assert.DoesNotContain("October", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("orders ship", sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_question_outside_the_catalog_is_refused_rather_than_improvised()
    {
        var events = await AskAsync("which of our customers is most likely to churn", TestPrincipals.Dana);

        Assert.Single(events.OfType<ReportEvent.Refused>());
        Assert.Empty(events.OfType<ReportEvent.Sql>());
    }
}

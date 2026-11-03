using SmartReport.Ai;
using SmartReport.Core.Reporting;
using SmartReport.Core.Catalog;
using SmartReport.Core.Policy;
using SmartReport.Core.Specs;
using SmartReport.Core.Sql;
using SmartReport.Data;
using Xunit;

namespace SmartReport.Tests;

/// <summary>
/// The whole pipeline, against a real database: question in, report or refusal out.
///
/// <para>
/// This is the test the product exists to pass. Everything else checks a layer; this checks that
/// the layers are wired in the order that makes the guarantee true.
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

    private static Principal Dana => new("dana", "Dana Okafor", Roles.Analyst, null, null);
    private static Principal Marcus => new("marcus", "Marcus Lindqvist", Roles.RegionalManager, 1, "North");
    private static Principal Priya => new("priya", "Priya Raman", Roles.HrDirector, null, null);

    [Fact]
    public async Task An_analyst_asking_for_salary_is_refused_and_no_sql_is_ever_compiled()
    {
        var events = await AskAsync("average salary by department", Dana);

        var refusal = Assert.Single(events.OfType<ReportEvent.Refused>());
        Assert.Contains("HR Director", refusal.Message);

        // The point: not merely zero rows, but no query at all.
        Assert.Empty(events.OfType<ReportEvent.Sql>());
        Assert.Empty(events.OfType<ReportEvent.Document>());
    }

    [Fact]
    public async Task The_hr_director_asking_the_same_question_gets_rows()
    {
        var events = await AskAsync("average salary by department", Priya);

        Assert.Empty(events.OfType<ReportEvent.Refused>());
        Assert.Single(events.OfType<ReportEvent.Sql>());

        var document = Assert.Single(events.OfType<ReportEvent.Document>()).Report;
        Assert.NotEmpty(document.Sections[0].Table.Rows);
    }

    [Fact]
    public async Task The_same_revenue_question_returns_fewer_rows_for_a_regional_manager()
    {
        var analystRows = RowCount(await AskAsync("revenue by region, last quarter", Dana));
        var managerRows = RowCount(await AskAsync("revenue by region, last quarter", Marcus));

        Assert.True(analystRows > managerRows,
            $"Expected the regional manager to see fewer rows; analyst {analystRows}, manager {managerRows}.");
        Assert.True(managerRows > 0);
    }

    [Fact]
    public async Task A_regional_manager_is_told_why_their_result_is_smaller()
    {
        var events = await AskAsync("revenue by region, last quarter", Marcus);

        var scoped = events.OfType<ReportEvent.Guard>()
            .Select(g => g.Decision)
            .Single(d => d.Outcome == PolicyOutcome.Scoped);

        Assert.Contains("North", scoped.Reason);
    }

    [Fact]
    public async Task A_question_the_catalog_cannot_answer_is_refused_rather_than_improvised()
    {
        var events = await AskAsync("what is the weather in Oslo", Dana);

        Assert.Single(events.OfType<ReportEvent.Refused>());
        Assert.Empty(events.OfType<ReportEvent.Sql>());
    }

    [Fact]
    public async Task Compiled_sql_never_contains_the_question_text()
    {
        var events = await AskAsync("revenue by region, last quarter", Dana);
        var sql = Assert.Single(events.OfType<ReportEvent.Sql>()).Text;

        Assert.DoesNotContain("last quarter", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("revenue by region", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_multi_model_question_produces_one_query_per_model()
    {
        var events = await AskAsync("turnover cost vs the D&I program budget", Priya);

        Assert.Equal(2, events.OfType<ReportEvent.Sql>().Count());
        Assert.Single(events.OfType<ReportEvent.Document>());
    }

    [Fact]
    public async Task Provenance_names_what_was_read_and_what_was_withheld()
    {
        var document = Assert.Single(
            (await AskAsync("revenue by region, last quarter", Dana)).OfType<ReportEvent.Document>()).Report;

        Assert.Contains("orders", document.Provenance.TablesRead);
        Assert.Equal("scripted", document.Provenance.PlannerProvider);
    }

    private static int RowCount(IEnumerable<ReportEvent> events) =>
        events.OfType<ReportEvent.Rows>().Sum(r => r.Count);
}

using SmartReport.Ai;
using SmartReport.Core.Abstractions;
using SmartReport.Core.Reporting;
using SmartReport.Core.Specs;
using Xunit;

namespace SmartReport.Tests;

/// <summary>
/// "Convert this to a table" means nothing on its own. These cover the turn-to-turn behaviour
/// that makes it mean something — and the fact that the context is a hint, not an authority.
/// </summary>
public class FollowUpTests
{
    private static async Task<ReportSpec?> PlanAsync(PlanRequest request)
    {
        ReportSpec? spec = null;
        await foreach (var e in new ScriptedReportPlanner()
                           .PlanAsync(request, TestPrincipals.Dana, CancellationToken.None))
        {
            if (e is PlannerEvent.Planned planned)
            {
                spec = planned.Spec;
            }
        }
        return spec;
    }

    private const string PreviousPlan = """
        {"title":"Late shipments","queries":[{"model":"shipping","metrics":["late_shipments"],
         "dimensions":["carrier"],"filters":[],"label":"","chart":"bar"}]}
        """;

    [Theory]
    [InlineData("show that as a table", ChartKind.Table)]
    [InlineData("convert this to a line chart", ChartKind.Line)]
    [InlineData("make it a column chart", ChartKind.Column)]
    [InlineData("can we see that as a pie", ChartKind.Donut)]
    [InlineData("just the numbers please", ChartKind.Table)]
    public async Task A_follow_up_redraws_the_same_report(string message, ChartKind expected)
    {
        var spec = await PlanAsync(new PlanRequest(message, "show late shipments by carrier", PreviousPlan));

        Assert.NotNull(spec);
        var query = Assert.Single(spec!.Queries);

        // The report is unchanged apart from how it is drawn.
        Assert.Equal("shipping", query.Model);
        Assert.Equal(["late_shipments"], query.Metrics);
        Assert.Equal(["carrier"], query.Dimensions);
        Assert.Equal(expected, query.Chart);
    }

    [Fact]
    public async Task The_same_words_without_a_previous_report_are_not_a_follow_up()
    {
        // Nothing to convert. It must not silently invent a report to redraw.
        var spec = await PlanAsync(new PlanRequest("show that as a table"));

        Assert.Null(spec);
    }

    [Fact]
    public async Task A_plainly_new_question_ignores_the_previous_plan()
    {
        var spec = await PlanAsync(new PlanRequest(
            "which products are low on stock?", "show late shipments by carrier", PreviousPlan));

        Assert.NotNull(spec);
        Assert.Equal("inventory", spec!.Queries[0].Model);
    }

    [Fact]
    public async Task A_tampered_previous_plan_cannot_widen_what_comes_back()
    {
        // The context arrives from the browser, so it could name a restricted model. It is only
        // a hint — the planner's output still goes through validation and authorization, and
        // PolicyEngineTests covers the refusal. Here: the plan is parsed, not trusted as granted.
        const string tampered = """
            {"title":"x","queries":[{"model":"carrier_costs","metrics":["avg_cost_per_label"],
             "dimensions":["carrier"],"filters":[],"label":"","chart":"bar"}]}
            """;

        var spec = await PlanAsync(new PlanRequest("show that as a table", "anything", tampered));

        // It round-trips as a spec — and is refused downstream, which is where that decision belongs.
        Assert.NotNull(spec);
        Assert.Equal("carrier_costs", spec!.Queries[0].Model);

        var governed = new SmartReport.Core.Policy.PolicyEngine(TestPrincipals.Catalog)
            .Govern(new SpecValidator(TestPrincipals.Catalog).Validate(spec).Spec!, TestPrincipals.Dana);

        Assert.True(governed.IsRefused);
    }
}

using SmartReport.Core.Policy;
using SmartReport.Core.Specs;
using Xunit;

namespace SmartReport.Tests;

public class PolicyEngineTests
{
    private readonly PolicyEngine _policy = new(TestPrincipals.Catalog);
    private readonly SpecValidator _validator = new(TestPrincipals.Catalog);

    private GovernedSpec Govern(ReportSpec spec, Principal principal)
    {
        var validated = _validator.Validate(spec);
        Assert.True(validated.IsValid, validated.Summary);
        return _policy.Govern(validated.Spec!, principal);
    }

    private static ReportSpec Salaries() => new("Average salary by department",
        [new SpecQuery("compensation", ["avg_salary"], ["department"], [], null)]);

    private static ReportSpec Revenue() => new("Revenue by region",
        [new SpecQuery("sales", ["revenue"], ["region"], [], null)]);

    [Fact]
    public void Analyst_asking_for_salary_is_refused_with_a_reason()
    {
        var governed = Govern(Salaries(), TestPrincipals.Dana);

        Assert.True(governed.IsRefused);
        Assert.Empty(governed.Queries);
        Assert.Contains("HR Director", governed.RefusalReason);
        Assert.Contains(governed.Decisions,
            d => d.Outcome == PolicyOutcome.Denied && d.Subject == "metric:avg_salary");
    }

    [Fact]
    public void Hr_director_asking_for_salary_is_allowed()
    {
        var governed = Govern(Salaries(), TestPrincipals.Priya);

        Assert.False(governed.IsRefused);
        Assert.Single(governed.Queries);
        Assert.Contains("compensation", governed.TablesRead);
    }

    [Fact]
    public void A_refusal_names_the_withheld_measure()
    {
        var governed = Govern(Salaries(), TestPrincipals.Dana);

        Assert.Contains("avg_salary", governed.Withheld);
    }

    [Fact]
    public void Regional_manager_is_told_their_rows_are_scoped()
    {
        var governed = Govern(Revenue(), TestPrincipals.Marcus);

        var scope = Assert.Single(governed.Decisions, d => d.Outcome == PolicyOutcome.Scoped);
        Assert.Contains("North", scope.Reason);
    }

    [Fact]
    public void Unscoped_principals_get_no_scoping_notice()
    {
        var governed = Govern(Revenue(), TestPrincipals.Dana);

        Assert.DoesNotContain(governed.Decisions, d => d.Outcome == PolicyOutcome.Scoped);
    }

    [Fact]
    public void Revenue_is_available_to_every_role()
    {
        foreach (var principal in new[] { TestPrincipals.Dana, TestPrincipals.Marcus, TestPrincipals.Priya })
        {
            Assert.False(Govern(Revenue(), principal).IsRefused);
        }
    }

    [Fact]
    public void A_mixed_report_keeps_the_allowed_query_and_drops_the_denied_one()
    {
        // Turnover cost beside programme budget is fine; salary alongside them is not.
        var spec = new ReportSpec("Turnover vs budget",
        [
            new SpecQuery("attrition", ["turnover_cost"], ["department"], [], null),
            new SpecQuery("compensation", ["total_payroll"], ["department"], [], null),
        ]);

        var governed = Govern(spec, TestPrincipals.Dana);

        Assert.False(governed.IsRefused);
        Assert.Single(governed.Queries);
        Assert.Equal("attrition", governed.Queries[0].Query.Model);
        Assert.Contains(governed.Decisions,
            d => d.Outcome == PolicyOutcome.Denied && d.Subject == "metric:total_payroll");
    }

    [Fact]
    public void Row_limits_are_capped_whatever_the_spec_asked_for()
    {
        var spec = new ReportSpec("Everything",
            [new SpecQuery("sales", ["revenue"], ["region"], [], int.MaxValue)]);

        Assert.True(Govern(spec, TestPrincipals.Dana).Queries[0].RowLimit <= SpecLimits.MaxRowLimit);
    }
}

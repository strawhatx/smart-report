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

    private static ReportSpec Billing() => new("Fees",
        [new SpecQuery("billing", ["total_fees"], ["fee_type"], [], null)]);

    private static ReportSpec Shipping() => new("Shipping",
        [new SpecQuery("shipping", ["orders_shipped"], ["carrier"], [], null)]);

    private static ReportSpec CarrierCosts() => new("Costs",
        [new SpecQuery("carrier_costs", ["avg_cost_per_label"], ["carrier"], [], null)]);

    [Fact]
    public void The_coordinator_is_refused_billing_and_pointed_at_the_owner()
    {
        var governed = Govern(Billing(), TestPrincipals.Dana);

        Assert.True(governed.IsRefused);
        var denial = Assert.Single(governed.Decisions, d => d.Outcome == PolicyOutcome.Denied);
        Assert.Contains("account owner", denial.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Priya Shah", denial.Remedy!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_account_owner_is_allowed_billing()
    {
        Assert.False(Govern(Billing(), TestPrincipals.Priya).IsRefused);
    }

    [Fact]
    public void Northpoint_internal_costs_are_refused_to_every_client_role()
    {
        foreach (var principal in new[] { TestPrincipals.Dana, TestPrincipals.Priya })
        {
            var governed = Govern(CarrierCosts(), principal);

            Assert.True(governed.IsRefused);
            var denial = Assert.Single(governed.Decisions, d => d.Outcome == PolicyOutcome.Denied);
            Assert.Contains("internal", denial.Reason, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_refusal_only_suggests_an_alternative_the_asker_may_actually_use()
    {
        // carrier_costs points at shipping.billed_cost_per_shipment, which both roles can see.
        var denial = Assert.Single(
            Govern(CarrierCosts(), TestPrincipals.Dana).Decisions,
            d => d.Outcome == PolicyOutcome.Denied);

        Assert.Contains("billed cost per shipment", denial.Remedy!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Shipping_is_available_to_both_roles()
    {
        foreach (var principal in new[] { TestPrincipals.Dana, TestPrincipals.Priya })
        {
            Assert.False(Govern(Shipping(), principal).IsRefused);
        }
    }

    [Fact]
    public void Every_answered_report_states_whose_data_it_read()
    {
        var scope = Assert.Single(
            Govern(Shipping(), TestPrincipals.Dana).Decisions,
            d => d.Outcome == PolicyOutcome.Scoped);

        Assert.Contains("Trailhead Coffee Co.", scope.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_mixed_report_keeps_what_is_allowed_and_drops_what_is_not()
    {
        var spec = new ReportSpec("Mixed",
        [
            new SpecQuery("shipping", ["orders_shipped"], ["carrier"], [], null),
            new SpecQuery("billing", ["total_fees"], ["fee_type"], [], null),
        ]);

        var governed = Govern(spec, TestPrincipals.Dana);

        Assert.False(governed.IsRefused);
        Assert.Single(governed.Queries);
        Assert.Equal("shipping", governed.Queries[0].Query.Model);
    }

    [Fact]
    public void Row_limits_are_capped_whatever_was_asked_for()
    {
        var spec = new ReportSpec("Everything",
            [new SpecQuery("shipping", ["orders_shipped"], ["carrier"], [], int.MaxValue)]);

        Assert.True(Govern(spec, TestPrincipals.Dana).Queries[0].RowLimit <= SpecLimits.MaxRowLimit);
    }
}

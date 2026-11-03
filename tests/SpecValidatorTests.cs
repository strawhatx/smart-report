using SmartReport.Core.Specs;
using Xunit;

namespace SmartReport.Tests;

public class SpecValidatorTests
{
    private readonly SpecValidator _validator = new(TestPrincipals.Catalog);

    private static ReportSpec Spec(params SpecQuery[] queries) => new("Test", queries);

    private static SpecQuery Query(
        string model = "sales",
        string[]? metrics = null,
        string[]? dimensions = null,
        SpecFilter[]? filters = null,
        int? limit = null) =>
        new(model, metrics ?? ["revenue"], dimensions ?? [], filters ?? [], limit);

    [Fact]
    public void Accepts_a_well_formed_spec()
    {
        var result = _validator.Validate(Spec(Query(dimensions: ["region"])));

        Assert.True(result.IsValid);
        Assert.Equal("sales", result.Spec!.Queries[0].Model);
    }

    [Fact]
    public void Rejects_an_unknown_model()
    {
        var result = _validator.Validate(Spec(Query(model: "salaries_but_sneaky")));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == SpecProblemKind.UnknownModel);
    }

    [Fact]
    public void Rejects_an_unknown_metric()
    {
        var result = _validator.Validate(Spec(Query(metrics: ["revenue", "profit_margin"])));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == SpecProblemKind.UnknownMetric);
    }

    [Fact]
    public void Rejects_an_unknown_dimension()
    {
        var result = _validator.Validate(Spec(Query(dimensions: ["salesperson_home_address"])));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == SpecProblemKind.UnknownDimension);
    }

    [Fact]
    public void Rejects_an_unknown_filter_field()
    {
        var result = _validator.Validate(Spec(Query(
            filters: [new SpecFilter("secret_flag", FilterOp.Eq, ["1"])])));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == SpecProblemKind.UnknownFilterField);
    }

    [Fact]
    public void Rejects_an_operator_the_field_type_does_not_support()
    {
        // ordered_at is a date; 'contains' is a text operator.
        var result = _validator.Validate(Spec(Query(
            filters: [new SpecFilter("ordered_at", FilterOp.Contains, ["2025"])])));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == SpecProblemKind.BadOperator);
    }

    [Fact]
    public void Rejects_a_value_that_does_not_parse_as_the_declared_type()
    {
        var result = _validator.Validate(Spec(Query(
            filters: [new SpecFilter("ordered_at", FilterOp.Gte, ["last tuesday-ish"])])));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == SpecProblemKind.BadValue);
    }

    [Fact]
    public void Rejects_between_with_the_wrong_number_of_values()
    {
        var result = _validator.Validate(Spec(Query(
            filters: [new SpecFilter("ordered_at", FilterOp.Between, ["2025-01-01"])])));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == SpecProblemKind.BadValue);
    }

    [Fact]
    public void Rejects_a_query_with_no_metrics()
    {
        var result = _validator.Validate(Spec(Query(metrics: [])));

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == SpecProblemKind.Empty);
    }

    [Fact]
    public void Rejects_an_empty_plan()
    {
        Assert.False(_validator.Validate(new ReportSpec("Nothing", [])).IsValid);
        Assert.False(_validator.Validate(null).IsValid);
    }

    [Fact]
    public void Clamps_the_row_limit_rather_than_honouring_a_huge_one()
    {
        var result = _validator.Validate(Spec(Query(limit: 5_000_000)));

        Assert.True(result.IsValid);
        Assert.Equal(SpecLimits.MaxRowLimit, result.Spec!.Queries[0].Limit);
    }

    [Fact]
    public void Applies_a_default_row_limit_when_none_is_given()
    {
        var result = _validator.Validate(Spec(Query(limit: null)));

        Assert.Equal(SpecLimits.DefaultRowLimit, result.Spec!.Queries[0].Limit);
    }

    [Fact]
    public void Drops_duplicate_keys_without_failing()
    {
        var result = _validator.Validate(Spec(Query(metrics: ["revenue", "revenue"])));

        Assert.True(result.IsValid);
        Assert.Single(result.Spec!.Queries[0].Metrics);
    }
}

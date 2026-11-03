using SmartReport.Ai;
using SmartReport.Core.Specs;
using Xunit;

namespace SmartReport.Tests;

/// <summary>
/// What happens when the model returns something hostile, malformed, or merely wrong.
///
/// <para>
/// A local model is not a trusted component. These assert that the boundary holds regardless of
/// what comes back: parsing is lenient about shape, the validator is strict about meaning, and
/// nothing reaches the compiler that didn't resolve to a catalog entry.
/// </para>
/// </summary>
public class PlannerContractTests
{
    private readonly SpecValidator _validator = new(TestPrincipals.Catalog);

    private SpecValidationResult ParseAndValidate(string json) =>
        _validator.Validate(PlannerJson.Parse(json).Spec);

    [Fact]
    public void A_sql_injection_attempt_in_a_metric_key_is_rejected_as_an_unknown_metric()
    {
        var result = ParseAndValidate("""
            {"title":"x","queries":[{"model":"sales","metrics":["'; DROP TABLE orders; --"],
             "dimensions":["region"],"filters":[]}]}
            """);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == SpecProblemKind.UnknownMetric);
    }

    [Fact]
    public void An_invented_model_is_rejected()
    {
        var result = ParseAndValidate("""
            {"queries":[{"model":"pg_catalog.pg_user","metrics":["revenue"],"dimensions":[]}]}
            """);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == SpecProblemKind.UnknownModel);
    }

    [Fact]
    public void Malformed_json_does_not_throw()
    {
        var parsed = PlannerJson.Parse("this is not json at all {{{");

        Assert.Null(parsed.Spec);
        Assert.False(parsed.NoMatch);
        Assert.NotNull(parsed.Message);
    }

    [Fact]
    public void Json_wrapped_in_prose_or_a_code_fence_is_still_read()
    {
        // Local models commonly do this. Refusing it would be pedantry — the content is
        // validated either way.
        var parsed = PlannerJson.Parse("""
            Sure! Here's the plan:
            ```json
            {"title":"Revenue","queries":[{"model":"sales","metrics":["revenue"],"dimensions":["region"]}]}
            ```
            """);

        Assert.NotNull(parsed.Spec);
        Assert.Equal("sales", parsed.Spec!.Queries[0].Model);
    }

    [Fact]
    public void A_single_query_envelope_is_accepted_as_well_as_a_list()
    {
        var parsed = PlannerJson.Parse("""
            {"title":"Revenue","model":"sales","metrics":["revenue"],"dimensions":["region"]}
            """);

        Assert.NotNull(parsed.Spec);
        Assert.Single(parsed.Spec!.Queries);
    }

    [Fact]
    public void An_explicit_no_match_is_carried_through()
    {
        var parsed = PlannerJson.Parse("""{"no_match":true,"message":"Nothing about weather here."}""");

        Assert.True(parsed.NoMatch);
        Assert.Equal("Nothing about weather here.", parsed.Message);
    }

    [Fact]
    public void A_missing_model_name_becomes_an_unknown_model_rather_than_a_guess()
    {
        var result = ParseAndValidate("""{"queries":[{"metrics":["revenue"],"dimensions":[]}]}""");

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Kind == SpecProblemKind.UnknownModel);
    }

    [Fact]
    public void An_enormous_limit_is_clamped_rather_than_honoured()
    {
        var result = ParseAndValidate("""
            {"queries":[{"model":"sales","metrics":["revenue"],"dimensions":["region"],"limit":999999999}]}
            """);

        Assert.True(result.IsValid);
        Assert.Equal(SpecLimits.MaxRowLimit, result.Spec!.Queries[0].Limit);
    }

    [Fact]
    public void A_filter_value_that_looks_like_sql_survives_as_text_and_is_bound()
    {
        // The validator accepts it — it is a legal string — and the compiler binds it. The
        // SqlCompiler tests assert it never becomes SQL text.
        var result = ParseAndValidate("""
            {"queries":[{"model":"sales","metrics":["revenue"],"dimensions":["region"],
             "filters":[{"field":"region","op":"eq","values":["North' OR 1=1 --"]}]}]}
            """);

        Assert.True(result.IsValid);
        Assert.Equal("North' OR 1=1 --", result.Spec!.Queries[0].Filters[0].Values[0]);
    }
}

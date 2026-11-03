using System.Runtime.CompilerServices;
using SmartReport.Core.Abstractions;
using SmartReport.Core.Policy;
using SmartReport.Core.Specs;

namespace SmartReport.Ai;

/// <summary>
/// A deterministic planner that matches a question against a small table of known shapes.
///
/// <para>
/// This is the default so the demo runs anywhere with no model to install, and so the governance
/// behaviour can be tested without a model's variability in the way. It is <b>not</b> pretending
/// to be a local model: the UI names the provider, and the Ollama planner is a configuration
/// change away.
/// </para>
/// <para>
/// It answers only what it recognises. An unrecognised question returns
/// <see cref="PlannerEvent.NoMatch"/> — the same honest refusal a real planner gives when nothing
/// in the catalog fits.
/// </para>
/// </summary>
public sealed class ScriptedReportPlanner : IReportPlanner
{
    public string ProviderName => "scripted";

    /// <summary>Paced so the stream looks like thinking rather than a page-load flicker.</summary>
    private static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(260);

    private sealed record Rule(
        string[] AllOf,
        string[] AnyOf,
        string Title,
        Func<SpecQuery[]> Build,
        string[] Narration);

    // The last complete quarter relative to the seeded data, which ends 2026-10-31.
    private const string LastQuarterStart = "2026-07-01";
    private const string LastQuarterEnd = "2026-10-01";

    private static readonly Rule[] Rules =
    [
        new(
            AllOf: ["salary"],
            AnyOf: ["department", "departments", "average", "avg", "by"],
            Title: "Average salary by department",
            Build: () => [new SpecQuery("compensation", ["avg_salary"], ["department"], [], null)],
            Narration:
            [
                "Reading the question: a measure of pay, grouped by department.",
                "Matching 'salary' to the compensation model.",
                "Selecting avg_salary grouped by department.",
            ]),

        new(
            AllOf: ["turnover"],
            AnyOf: ["budget", "program", "programme", "d&i", "diversity", "inclusion", "cost"],
            Title: "Turnover cost against programme budget",
            Build: () =>
            [
                new SpecQuery("attrition", ["turnover_cost", "attrition_count"], ["department"], [], null),
                new SpecQuery("programs", ["program_budget", "program_spend", "budget_remaining"],
                    ["program"], [new SpecFilter("fiscal_year", FilterOp.Eq, ["2026"])], null),
            ],
            Narration:
            [
                "This spans two models: attrition and programmes.",
                "Planning turnover cost by department.",
                "Planning programme budget and spend for the current fiscal year.",
            ]),

        new(
            AllOf: ["revenue"],
            AnyOf: ["region", "regions"],
            Title: "Revenue by region",
            Build: () =>
            [
                new SpecQuery("sales", ["revenue", "order_count"], ["region"],
                    [new SpecFilter("ordered_at", FilterOp.Between, [LastQuarterStart, LastQuarterEnd])], null),
            ],
            Narration:
            [
                "Reading the question: revenue, grouped by region.",
                "Matching to the sales model.",
                $"Applying a date range: {LastQuarterStart} to {LastQuarterEnd}.",
            ]),

        new(
            AllOf: ["revenue"],
            AnyOf: ["month", "monthly", "time", "trend", "over"],
            Title: "Revenue by month",
            Build: () => [new SpecQuery("sales", ["revenue"], ["month"], [], null)],
            Narration: ["Revenue over time.", "Grouping by month on the sales model."]),

        new(
            AllOf: ["revenue"],
            AnyOf: ["product", "products", "category", "categories"],
            Title: "Revenue by product category",
            Build: () => [new SpecQuery("sales", ["revenue", "units_sold"], ["product_category"], [], null)],
            Narration: ["Revenue broken down by what was sold.", "Grouping by product category."]),

        new(
            AllOf: ["headcount"],
            AnyOf: ["department", "region", "month", "by"],
            Title: "Headcount by department",
            Build: () => [new SpecQuery("workforce", ["headcount"], ["department"], [], null)],
            Narration: ["Matching 'headcount' to the workforce model.", "Grouping by department."]),

        new(
            AllOf: ["attrition"],
            AnyOf: ["reason", "reasons", "why", "department"],
            Title: "Departures by reason",
            Build: () => [new SpecQuery("attrition", ["attrition_count", "turnover_cost"], ["reason"], [], null)],
            Narration: ["Matching to the attrition model.", "Grouping departures by stated reason."]),

        new(
            AllOf: ["revenue"],
            AnyOf: [],
            Title: "Total revenue",
            Build: () => [new SpecQuery("sales", ["revenue", "order_count", "avg_order_value"], [], [], null)],
            Narration: ["No grouping asked for — reporting headline totals."]),
    ];

    public async IAsyncEnumerable<PlannerEvent> PlanAsync(
        string question,
        Principal principal,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var normalized = question.ToLowerInvariant();

        yield return new PlannerEvent.Thinking($"Reading the catalog as {Roles.Label(principal.Role)}.");
        await Task.Delay(StepDelay, cancellationToken);

        var rule = Rules.FirstOrDefault(r => Matches(r, normalized));

        if (rule is null)
        {
            yield return new PlannerEvent.Thinking("Nothing in the catalog matches that.");
            await Task.Delay(StepDelay, cancellationToken);
            yield return new PlannerEvent.NoMatch(
                "I can't answer that from the catalog yet. Try revenue, orders, headcount, "
                + "attrition, turnover cost, programme budgets — or salary, if your role allows it.");
            yield break;
        }

        foreach (var line in rule.Narration)
        {
            yield return new PlannerEvent.Thinking(line);
            await Task.Delay(StepDelay, cancellationToken);
        }

        yield return new PlannerEvent.Planned(new ReportSpec(rule.Title, rule.Build()));
    }

    private static bool Matches(Rule rule, string question) =>
        rule.AllOf.All(question.Contains)
        && (rule.AnyOf.Length == 0 || rule.AnyOf.Any(question.Contains));
}

using System.Runtime.CompilerServices;
using SmartReport.Core.Abstractions;
using SmartReport.Core.Policy;
using SmartReport.Core.Specs;

namespace SmartReport.Ai;

/// <summary>
/// A deterministic planner that matches a question against a table of known shapes.
///
/// <para>
/// The default, so the demo runs anywhere with no key and no model to install, and so the
/// governance behaviour can be tested without a model's variability in the way. It is not
/// pretending to be an LLM: the UI names the provider on every report.
/// </para>
/// <para>
/// It answers only what it recognises. Anything else returns <see cref="PlannerEvent.NoMatch"/> —
/// the same honest refusal a real planner gives when nothing in the catalog fits.
/// </para>
/// </summary>
public sealed class ScriptedReportPlanner : IReportPlanner
{
    public string ProviderName => "scripted";

    private static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(240);

    /// <summary>The month the demo data ends on.</summary>
    private const string MonthStart = "2026-10-01";
    private const string MonthEnd = "2026-11-01";

    private sealed record Rule(string[] AllOf, string[] AnyOf, string Title, string[] Narration, Func<SpecQuery[]> Build);

    private const string PriorMonthStart = "2026-09-01";
    private const string PriorMonthEnd = "2026-10-01";

    private static SpecFilter October(string field) =>
        new(field, FilterOp.Between, [MonthStart, MonthEnd]);

    private static SpecFilter September(string field) =>
        new(field, FilterOp.Between, [PriorMonthStart, PriorMonthEnd]);

    private static readonly Rule[] Rules =
    [
        // ── The benchmark. The question a client really asks, and the one a naive text-to-SQL
        // bot answers by reading other customers' rows.
        new(
            AllOf: [],
            AnyOf: ["compare", "comparison", "benchmark", "other clients", "peers", "industry", "average for"],
            Title: "On-time rate against other Northpoint clients",
            Narration:
            [
                "Two parts: your own figure, and the peer cohort.",
                "Your on-time rate comes from your shipments.",
                "Peers come from the benchmarks model — anonymized cohort totals only.",
            ],
            Build: () =>
            [
                new SpecQuery("shipping", ["on_time_rate", "orders_shipped"], [],
                    [October("shipped_at")], null, "headline"),
                new SpecQuery("benchmarks", ["peer_median", "peer_p25", "peer_p75", "cohort_size"], [],
                    [new SpecFilter("metric", FilterOp.Eq, ["on_time_rate"]), October("period_month")],
                    null, "headline"),
            ]),

        // ── Northpoint's own commercial data.
        new(
            AllOf: [],
            AnyOf: ["northpoint pay", "pay ups", "pay the carrier", "contract rate", "cost per label", "what does northpoint"],
            Title: "Carrier contract rates",
            Narration: ["That asks for Northpoint's own carrier pricing.", "Checking whether it is yours to see."],
            Build: () => [new SpecQuery("carrier_costs", ["avg_cost_per_label"], ["carrier"], [], null)]),

        // ── Billing: the account owner's.
        new(
            AllOf: [],
            AnyOf: ["fee", "fees", "invoice", "billing", "what are we paying", "charges"],
            Title: "Fees and invoice breakdown",
            Narration: ["Matching to the billing model.", "Grouping charges by fee type."],
            Build: () =>
            [
                new SpecQuery("billing", ["total_fees"], ["fee_type"],
                    [October("period_month")], null),
            ]),

        // ── The default report, and what the Library's first template opens.
        new(
            AllOf: [],
            AnyOf: ["how did our orders ship", "shipping report", "october", "ship in october"],
            Title: "October 2026 Shipping Report",
            Narration:
            [
                "Reading the question: October, shipping performance.",
                "Headline figures first — volume, on-time rate, time to ship.",
                "Then late shipments by carrier, and orders by channel.",
                "Finally this year against last year, month by month.",
            ],
            Build: () =>
            [
                new SpecQuery("shipping", ["orders_shipped", "on_time_rate", "avg_days_to_ship"], [],
                    [October("shipped_at")], null, "headline"),
                // Same measures, previous month. The UI folds this into the "vs September" deltas
                // rather than drawing it, so the comparison is a real figure, not an estimate.
                new SpecQuery("shipping", ["orders_shipped", "on_time_rate", "avg_days_to_ship"], [],
                    [September("shipped_at")], null, "comparison:September"),
                new SpecQuery("inventory", ["low_stock_products"], [], [], null, "headline"),
                new SpecQuery("shipping", ["late_shipments"], ["carrier"], [October("shipped_at")], null),
                new SpecQuery("shipping", ["orders_shipped"], ["channel"], [October("shipped_at")], null),
                new SpecQuery("shipping", ["orders_shipped"], ["month"], [], 40),
            ]),

        new(
            AllOf: ["late"],
            AnyOf: ["carrier", "carriers", "who", "why"],
            Title: "Late shipments by carrier",
            Narration: ["Matching to the shipping model.", "Counting late shipments per carrier."],
            Build: () => [new SpecQuery("shipping", ["late_shipments", "orders_shipped"], ["carrier"],
                [October("shipped_at")], null)]),

        new(
            AllOf: [],
            AnyOf: ["channel", "channels", "shopify", "amazon", "where do our orders come from"],
            Title: "Orders by sales channel",
            Narration: ["Breaking volume down by where the order came from."],
            Build: () => [new SpecQuery("shipping", ["orders_shipped"], ["channel"],
                [October("shipped_at")], null)]),

        new(
            AllOf: [],
            AnyOf: ["low stock", "running out", "reorder", "inventory", "stock"],
            Title: "Stock running low",
            Narration: ["Matching to the inventory model.", "Finding products at or below their reorder point."],
            Build: () => [new SpecQuery("inventory", ["low_stock_products", "units_on_hand", "products_tracked"], [], [], null)]),

        new(
            AllOf: ["return"],
            AnyOf: [],
            Title: "Returns by reason",
            Narration: ["Matching to the returns model.", "Grouping by the stated reason."],
            Build: () => [new SpecQuery("returns", ["return_count", "refund_value"], ["reason"], [], null)]),

        new(
            AllOf: [],
            AnyOf: ["billed per shipment", "cost per shipment", "shipping cost", "what are we billed"],
            Title: "Billed cost per shipment",
            Narration: ["This is your own billed figure, from your shipments."],
            Build: () => [new SpecQuery("shipping", ["billed_cost_per_shipment", "orders_shipped"], ["carrier"],
                [October("shipped_at")], null)]),

        new(
            AllOf: [],
            AnyOf: ["trend", "over time", "by month", "monthly", "last year", "vs 2025"],
            Title: "Orders shipped per month",
            Narration: ["Volume over time, this year against last."],
            Build: () => [new SpecQuery("shipping", ["orders_shipped"], ["month"], [], 40)]),

        new(
            AllOf: [],
            AnyOf: ["on time", "on-time", "how are we doing", "performance"],
            Title: "On-time performance",
            Narration: ["Headline shipping figures for the month."],
            Build: () => [new SpecQuery("shipping", ["on_time_rate", "orders_shipped", "late_shipments"], [],
                [October("shipped_at")], null)]),
    ];

    public async IAsyncEnumerable<PlannerEvent> PlanAsync(
        string question,
        Principal principal,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var normalized = question.ToLowerInvariant();

        yield return new PlannerEvent.Thinking(
            $"Reading the catalog as {principal.DisplayName}, {Roles.Label(principal.Role)} at {principal.ClientName}.");
        await Task.Delay(StepDelay, cancellationToken);

        var rule = Rules.FirstOrDefault(r => Matches(r, normalized));

        if (rule is null)
        {
            yield return new PlannerEvent.Thinking("Nothing in the catalog matches that.");
            await Task.Delay(StepDelay, cancellationToken);
            yield return new PlannerEvent.NoMatch(
                "I can't answer that from the catalog yet. Try shipping and on-time performance, "
                + "late shipments by carrier, orders by channel, inventory, returns, peer benchmarks — "
                + "or fees, if your role allows it.");
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

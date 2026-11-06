namespace SmartReport.Core.Catalog;

/// <summary>
/// The semantic layer. Everything the planner may reference lives here, and
/// <see cref="Sql.SqlCompiler"/> only ever emits identifiers it read out of this object.
///
/// <para>
/// Adding a model here is the only way to widen what the product can answer. That is deliberate:
/// an off-catalog question gets an honest refusal rather than an improvised query.
/// </para>
/// </summary>
public sealed class DataCatalog
{
    /// <summary>
    /// Minimum number of clients behind a benchmark figure.
    ///
    /// <para>
    /// Below this, a "peer median" is a thin disguise over a handful of named companies. The floor
    /// is compiled into every benchmark query and repeated by the table's row-level security.
    /// </para>
    /// </summary>
    public const int MinimumCohortSize = 20;

    /// <summary>
    /// A role no client user holds. Northpoint's own commercial data requires it, which is how
    /// that data sits in the same database as the portal without being reachable from it.
    /// </summary>
    public const string NorthpointStaffRole = "northpoint_staff";

    private readonly IReadOnlyDictionary<string, QueryModel> _models;

    public DataCatalog(IEnumerable<QueryModel> models) =>
        _models = models.ToDictionary(m => m.Key, StringComparer.Ordinal);

    public IReadOnlyCollection<QueryModel> Models => _models.Values.ToArray();

    public QueryModel? Model(string key) => _models.GetValueOrDefault(key);

    /// <summary>Resolves a <c>model.metric</c> key, as used by <c>Alternative</c>.</summary>
    public (QueryModel Model, MetricDefinition Metric)? ResolveMetric(string qualifiedKey)
    {
        var parts = qualifiedKey.Split('.', 2);
        if (parts.Length != 2)
        {
            return null;
        }

        var model = Model(parts[0]);
        var metric = model?.Metric(parts[1]);
        return model is not null && metric is not null ? (model, metric) : null;
    }

    /// <summary>
    /// Every model with a time axis also accepts this, mapped to that model's own date column.
    ///
    /// <para>
    /// Without it the planner has to remember that a date range is <c>shipped_at</c> here,
    /// <c>received_at</c> there and <c>period_month</c> somewhere else — and because the schema
    /// offers the union of filter names, picking the wrong one is both easy and allowed. One
    /// name that works everywhere removes the mistake rather than catching it.
    /// </para>
    /// </summary>
    public const string PeriodFilter = "period";

    /// <summary>
    /// What the planner is shown: keys, labels and types only.
    ///
    /// <para>
    /// SQL expressions are never included — the model has no business knowing the physical schema,
    /// and it does not need to in order to pick a measure. Restricted models are still listed, so
    /// that asking for one produces a legible refusal rather than a shrug.
    /// </para>
    /// </summary>
    public string DescribeForPlanner()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var model in _models.Values.OrderBy(m => m.Key, StringComparer.Ordinal))
        {
            sb.Append("model: ").Append(model.Key).Append(" — ").AppendLine(model.Description);
            sb.Append("  metrics: ")
              .AppendLine(string.Join(", ", model.Metrics.Values.Select(m => m.Key)));
            sb.Append("  dimensions: ")
              .AppendLine(string.Join(", ", model.Dimensions.Values.Select(d => d.Key)));
            sb.Append("  filters: ")
              .AppendLine(string.Join(", ", model.Filters.Values.Select(f => $"{f.Key}:{f.Type.ToString().ToLowerInvariant()}")));

            if (model.Filter(PeriodFilter) is not null)
            {
                sb.AppendLine("  (for a date range on this model, prefer 'period')");
            }
        }
        return sb.ToString();
    }

    /// <summary>The Northpoint demo catalog. Expressions here are the only SQL fragments in the system.</summary>
    public static DataCatalog CreateDefault() => new(
    [
        // ── Shipping: the model the default report is built from.
        new QueryModel(
            Key: "shipping",
            Label: "Shipping",
            Description: "Orders shipped, on-time performance, carriers and what you were billed.",
            From: """
                  shipments s
                  JOIN orders o ON o.id = s.order_id AND o.client_id = s.client_id
                  """,
            Metrics: Index(
                new MetricDefinition("orders_shipped", "Orders shipped", "COUNT(DISTINCT s.order_id)"),
                new MetricDefinition("on_time_rate", "Shipped on time",
                    "100.0 * (1 - SUM(CASE WHEN s.is_late THEN 1 ELSE 0 END)::numeric / NULLIF(COUNT(*), 0))",
                    "percent", IsAdditive: false),
                new MetricDefinition("late_shipments", "Late shipments",
                    "SUM(CASE WHEN s.is_late THEN 1 ELSE 0 END)"),
                new MetricDefinition("avg_days_to_ship", "Avg time to ship",
                    "AVG(EXTRACT(EPOCH FROM (s.shipped_at - o.ordered_at)) / 86400.0)", "days", IsAdditive: false),
                // The authorized answer to "what does Northpoint pay UPS" — this is the client's
                // own billed figure, which is theirs to see.
                new MetricDefinition("billed_cost_per_shipment", "Billed cost per shipment",
                    "AVG(s.billed_amount)", "currency", IsAdditive: false)),
            Dimensions: Index(
                new DimensionDefinition("carrier", "Carrier", "s.carrier"),
                new DimensionDefinition("service", "Service", "s.service"),
                new DimensionDefinition("on_time", "On time", 
                    "CASE WHEN s.is_late THEN 'Late' ELSE 'On time' END", IsPartToWhole: true),
                new DimensionDefinition("channel", "Sales channel", "o.channel", IsPartToWhole: true),
                new DimensionDefinition("month", "Month", "to_char(s.shipped_at, 'YYYY-MM')", IsTemporal: true),
                new DimensionDefinition("week", "Week", "to_char(s.shipped_at, 'IYYY-\"W\"IW')", IsTemporal: true)),
            Filters: Index(
                new FilterFieldDefinition("shipped_at", "Ship date", "s.shipped_at", FieldType.Date),
                new FilterFieldDefinition("period", "Period", "s.shipped_at", FieldType.Date),
                new FilterFieldDefinition("carrier", "Carrier", "s.carrier", FieldType.Text),
                new FilterFieldDefinition("channel", "Sales channel", "o.channel", FieldType.Text),
                new FilterFieldDefinition("is_late", "Late only", "s.is_late::text", FieldType.Text)),
            Tables: ["shipments", "orders"]),

        // ── Orders: demand rather than fulfilment.
        new QueryModel(
            Key: "orders",
            Label: "Orders",
            Description: "Orders received, by channel and status.",
            From: "orders o",
            Metrics: Index(
                new MetricDefinition("order_count", "Orders", "COUNT(*)"),
                new MetricDefinition("unshipped_orders", "Unshipped",
                    "SUM(CASE WHEN o.shipped_at IS NULL THEN 1 ELSE 0 END)")),
            Dimensions: Index(
                new DimensionDefinition("channel", "Sales channel", "o.channel", IsPartToWhole: true),
                new DimensionDefinition("status", "Status", "o.status"),
                new DimensionDefinition("month", "Month", "to_char(o.ordered_at, 'YYYY-MM')", IsTemporal: true)),
            Filters: Index(
                new FilterFieldDefinition("ordered_at", "Order date", "o.ordered_at", FieldType.Date),
                new FilterFieldDefinition("period", "Period", "o.ordered_at", FieldType.Date),
                new FilterFieldDefinition("channel", "Sales channel", "o.channel", FieldType.Text),
                new FilterFieldDefinition("status", "Status", "o.status", FieldType.Text)),
            Tables: ["orders"]),

        // ── Inventory.
        new QueryModel(
            Key: "inventory",
            Label: "Inventory",
            Description: "Stock on hand and what is about to run out.",
            From: "products p",
            Metrics: Index(
                new MetricDefinition("products_tracked", "Products", "COUNT(*)"),
                new MetricDefinition("units_on_hand", "Units on hand", "SUM(p.on_hand)"),
                new MetricDefinition("low_stock_products", "Low-stock products",
                    "SUM(CASE WHEN p.on_hand <= p.reorder_point THEN 1 ELSE 0 END)")),
            Dimensions: Index(
                new DimensionDefinition("product", "Product", "p.name"),
                new DimensionDefinition("sku", "SKU", "p.sku"),
                new DimensionDefinition("stock_status", "Stock status",
                    "CASE WHEN p.on_hand <= p.reorder_point THEN 'Low' ELSE 'OK' END",
                    IsPartToWhole: true)),
            Filters: Index(
                new FilterFieldDefinition("sku", "SKU", "p.sku", FieldType.Text),
                new FilterFieldDefinition("product", "Product", "p.name", FieldType.Text)),
            Tables: ["products"]),

        // ── Returns.
        new QueryModel(
            Key: "returns",
            Label: "Returns",
            Description: "Returned orders, their stated reasons and refund value.",
            From: """
                  returns r
                  JOIN orders o ON o.id = r.order_id AND o.client_id = r.client_id
                  """,
            Metrics: Index(
                new MetricDefinition("return_count", "Returns", "COUNT(*)"),
                new MetricDefinition("refund_value", "Refund value", "SUM(r.refund_amount)", "currency"),
                new MetricDefinition("avg_refund", "Average refund", "AVG(r.refund_amount)", "currency", IsAdditive: false)),
            Dimensions: Index(
                new DimensionDefinition("reason", "Reason", "r.reason"),
                new DimensionDefinition("channel", "Sales channel", "o.channel", IsPartToWhole: true),
                new DimensionDefinition("month", "Month", "to_char(r.received_at, 'YYYY-MM')", IsTemporal: true)),
            Filters: Index(
                new FilterFieldDefinition("received_at", "Received", "r.received_at", FieldType.Date),
                new FilterFieldDefinition("period", "Period", "r.received_at", FieldType.Date),
                new FilterFieldDefinition("reason", "Reason", "r.reason", FieldType.Text),
                new FilterFieldDefinition("channel", "Sales channel", "o.channel", FieldType.Text)),
            Tables: ["returns", "orders"]),

        // ── Billing: the client's own invoice, restricted to whoever owns the account.
        new QueryModel(
            Key: "billing",
            Label: "Billing",
            Description: "Monthly fees and invoice breakdown. Restricted to the account owner.",
            From: "fees f",
            Metrics: Index(
                // No Alternative: a per-shipment average is not a stand-in for an invoice, and
                // offering one would dodge the real answer — which is that access can be granted.
                new MetricDefinition("total_fees", "Total fees", "SUM(f.amount)", "currency",
                    RequiredRoles: [Entities_AccountOwner],
                    RestrictionNote: "Billing is restricted to the account owner."),
                new MetricDefinition("avg_monthly_fees", "Average monthly fees", "AVG(f.amount)", "currency",
                    RequiredRoles: [Entities_AccountOwner],
                    RestrictionNote: "Billing is restricted to the account owner.", IsAdditive: false)),
            Dimensions: Index(
                new DimensionDefinition("fee_type", "Fee type", "f.fee_type"),
                new DimensionDefinition("month", "Month", "to_char(f.period_month, 'YYYY-MM')", IsTemporal: true)),
            Filters: Index(
                new FilterFieldDefinition("period_month", "Period", "f.period_month", FieldType.Date),
                new FilterFieldDefinition("period", "Period", "f.period_month", FieldType.Date),
                new FilterFieldDefinition("fee_type", "Fee type", "f.fee_type", FieldType.Text)),
            Tables: ["fees"],
            RequiredRoles: [Entities_AccountOwner],
            RestrictionNote: "Billing is restricted to the account owner."),

        // ── Benchmarks: how you compare, without anyone's figures being recoverable.
        new QueryModel(
            Key: "benchmarks",
            Label: "Peer benchmarks",
            Description: "Anonymized medians across Northpoint clients. Cohort totals only — no client is identifiable.",
            From: "benchmark_metrics bm",
            Metrics: Index(
                new MetricDefinition("peer_median", "Peer median", "AVG(bm.p50)", "percent", IsAdditive: false),
                new MetricDefinition("peer_p25", "Peer 25th percentile", "AVG(bm.p25)", "percent", IsAdditive: false),
                new MetricDefinition("peer_p75", "Peer 75th percentile", "AVG(bm.p75)", "percent", IsAdditive: false),
                new MetricDefinition("cohort_size", "Clients in cohort", "MIN(bm.client_count)", IsAdditive: false)),
            Dimensions: Index(
                new DimensionDefinition("metric", "Metric", "bm.metric_key"),
                new DimensionDefinition("segment", "Segment", "bm.dimension_value"),
                new DimensionDefinition("month", "Month", "to_char(bm.period_month, 'YYYY-MM')", IsTemporal: true)),
            Filters: Index(
                new FilterFieldDefinition("metric", "Metric", "bm.metric_key", FieldType.Text),
                new FilterFieldDefinition("period_month", "Period", "bm.period_month", FieldType.Date),
                new FilterFieldDefinition("period", "Period", "bm.period_month", FieldType.Date)),
            Tables: ["benchmark_metrics"],
            MandatoryPredicates:
            [
                new MandatoryPredicate(
                    "bm.client_count >= {0}",
                    MinimumCohortSize,
                    $"Benchmarks only use anonymized totals from {MinimumCohortSize} or more clients, "
                    + "so no single client's numbers can be worked out."),
            ]),

        // ── Northpoint's own commercial data. Present, and unreachable from the portal.
        new QueryModel(
            Key: "carrier_costs",
            Label: "Carrier contract rates",
            Description: "What Northpoint pays carriers per label. Northpoint internal.",
            From: "carrier_contract_rates ccr",
            Metrics: Index(
                new MetricDefinition("avg_cost_per_label", "Cost per label", "AVG(ccr.cost_per_label)", "currency",
                    RequiredRoles: [NorthpointStaffRole],
                    RestrictionNote: "Carrier contract rates are Northpoint's internal data.",
                    Alternative: "shipping.billed_cost_per_shipment", IsAdditive: false)),
            Dimensions: Index(
                new DimensionDefinition("carrier", "Carrier", "ccr.carrier"),
                new DimensionDefinition("service", "Service", "ccr.service")),
            Filters: Index(
                new FilterFieldDefinition("carrier", "Carrier", "ccr.carrier", FieldType.Text)),
            Tables: ["carrier_contract_rates"],
            RequiredRoles: [NorthpointStaffRole],
            RestrictionNote: "Carrier contract rates are Northpoint's internal data."),
    ]);

    // Mirrors Entities.ClientRoles.AccountOwner. Duplicated as a constant rather than referenced,
    // because core must not depend on the data project.
    private const string Entities_AccountOwner = "account_owner";

    private static Dictionary<string, MetricDefinition> Index(params MetricDefinition[] items) =>
        items.ToDictionary(i => i.Key, StringComparer.Ordinal);

    private static Dictionary<string, DimensionDefinition> Index(params DimensionDefinition[] items) =>
        items.ToDictionary(i => i.Key, StringComparer.Ordinal);

    private static Dictionary<string, FilterFieldDefinition> Index(params FilterFieldDefinition[] items) =>
        items.ToDictionary(i => i.Key, StringComparer.Ordinal);
}

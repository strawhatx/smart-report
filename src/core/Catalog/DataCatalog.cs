namespace SmartReport.Core.Catalog;

/// <summary>
/// The semantic layer. Everything the planner is allowed to reference lives here, and
/// <see cref="Sql.SqlCompiler"/> will only ever emit identifiers it read out of this object.
///
/// <para>
/// Adding a model here is the <i>only</i> way to widen what the product can answer. That is
/// deliberate: an off-catalog question gets an honest refusal rather than an improvised query.
/// </para>
/// </summary>
public sealed class DataCatalog
{
    private readonly IReadOnlyDictionary<string, QueryModel> _models;

    public DataCatalog(IEnumerable<QueryModel> models) =>
        _models = models.ToDictionary(m => m.Key, StringComparer.Ordinal);

    public IReadOnlyCollection<QueryModel> Models => _models.Values.ToArray();

    public QueryModel? Model(string key) => _models.GetValueOrDefault(key);

    /// <summary>
    /// What the planner is shown: keys, labels and types only. SQL expressions are never
    /// included, because the model has no business knowing — or guessing at — the physical schema.
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
        }
        return sb.ToString();
    }

    /// <summary>The demo catalog. Expressions here are the only SQL fragments in the system.</summary>
    public static DataCatalog CreateDefault() => new(
    [
        new QueryModel(
            Key: "sales",
            Label: "Sales",
            Description: "Orders and revenue by region, product and time.",
            From: """
                  orders o
                  JOIN order_items oi ON oi.order_id = o.id
                  JOIN regions r ON r.id = o.region_id
                  JOIN products p ON p.id = oi.product_id
                  """,
            Metrics: Index(
                new MetricDefinition("revenue", "Revenue", "SUM(oi.quantity * oi.unit_price)", "currency"),
                new MetricDefinition("order_count", "Orders", "COUNT(DISTINCT o.id)"),
                new MetricDefinition("units_sold", "Units sold", "SUM(oi.quantity)"),
                new MetricDefinition("avg_order_value", "Average order value",
                    "SUM(oi.quantity * oi.unit_price) / NULLIF(COUNT(DISTINCT o.id), 0)", "currency")),
            Dimensions: Index(
                new DimensionDefinition("region", "Region", "r.name"),
                new DimensionDefinition("product_category", "Product category", "p.category"),
                new DimensionDefinition("product", "Product", "p.name"),
                new DimensionDefinition("order_status", "Order status", "o.status"),
                new DimensionDefinition("month", "Month", "to_char(o.ordered_at, 'YYYY-MM')", FieldType.Text, IsTemporal: true),
                new DimensionDefinition("quarter", "Quarter",
                    "to_char(o.ordered_at, 'YYYY\"Q\"Q')", FieldType.Text, IsTemporal: true)),
            Filters: Index(
                new FilterFieldDefinition("ordered_at", "Order date", "o.ordered_at", FieldType.Date),
                new FilterFieldDefinition("region", "Region", "r.name", FieldType.Text),
                new FilterFieldDefinition("product_category", "Product category", "p.category", FieldType.Text),
                new FilterFieldDefinition("order_status", "Order status", "o.status", FieldType.Text)),
            Tables: ["orders", "order_items", "regions", "products"]),

        new QueryModel(
            Key: "workforce",
            Label: "Workforce",
            Description: "Headcount by department, region and month.",
            From: """
                  headcount_monthly h
                  JOIN departments d ON d.id = h.department_id
                  JOIN regions r ON r.id = h.region_id
                  """,
            Metrics: Index(
                new MetricDefinition("headcount", "Headcount", "SUM(h.headcount)"),
                new MetricDefinition("avg_headcount", "Average headcount", "AVG(h.headcount)")),
            Dimensions: Index(
                new DimensionDefinition("department", "Department", "d.name"),
                new DimensionDefinition("region", "Region", "r.name"),
                new DimensionDefinition("month", "Month", "to_char(h.month, 'YYYY-MM')", IsTemporal: true),
                new DimensionDefinition("quarter", "Quarter", "to_char(h.month, 'YYYY\"Q\"Q')",
                    IsTemporal: true)),
            Filters: Index(
                new FilterFieldDefinition("month", "Month", "h.month", FieldType.Date),
                new FilterFieldDefinition("department", "Department", "d.name", FieldType.Text),
                new FilterFieldDefinition("region", "Region", "r.name", FieldType.Text)),
            Tables: ["headcount_monthly", "departments", "regions"]),

        new QueryModel(
            Key: "attrition",
            Label: "Attrition",
            Description: "People leaving: counts, reasons and replacement cost.",
            From: """
                  attrition_events a
                  JOIN departments d ON d.id = a.department_id
                  JOIN regions r ON r.id = a.region_id
                  """,
            Metrics: Index(
                new MetricDefinition("attrition_count", "Departures", "COUNT(a.id)"),
                new MetricDefinition("turnover_cost", "Turnover cost", "SUM(a.replacement_cost)", "currency"),
                new MetricDefinition("avg_replacement_cost", "Average replacement cost",
                    "AVG(a.replacement_cost)", "currency")),
            Dimensions: Index(
                new DimensionDefinition("department", "Department", "d.name"),
                new DimensionDefinition("region", "Region", "r.name"),
                new DimensionDefinition("reason", "Reason", "a.reason"),
                new DimensionDefinition("month", "Month", "to_char(a.event_date, 'YYYY-MM')", IsTemporal: true),
                new DimensionDefinition("quarter", "Quarter", "to_char(a.event_date, 'YYYY\"Q\"Q')",
                    IsTemporal: true)),
            Filters: Index(
                new FilterFieldDefinition("event_date", "Departure date", "a.event_date", FieldType.Date),
                new FilterFieldDefinition("department", "Department", "d.name", FieldType.Text),
                new FilterFieldDefinition("region", "Region", "r.name", FieldType.Text),
                new FilterFieldDefinition("reason", "Reason", "a.reason", FieldType.Text)),
            Tables: ["attrition_events", "departments", "regions"]),

        // Compensation is the model the demo refuses. Every metric is role-gated, so an Analyst
        // asking for salary is stopped by PolicyEngine before anything is compiled (not merely
        // filtered to nothing by RLS, which would be correct but illegible).
        new QueryModel(
            Key: "compensation",
            Label: "Compensation",
            Description: "Salary by department and region. Restricted.",
            From: """
                  compensation c
                  JOIN employees e ON e.id = c.employee_id
                  JOIN departments d ON d.id = e.department_id
                  JOIN regions r ON r.id = e.region_id
                  """,
            Metrics: Index(
                new MetricDefinition("avg_salary", "Average salary", "AVG(c.annual_salary)", "currency",
                    RequiredRoles: ["hr_director"]),
                new MetricDefinition("total_payroll", "Total payroll", "SUM(c.annual_salary)", "currency",
                    RequiredRoles: ["hr_director"]),
                new MetricDefinition("median_salary", "Median salary",
                    "PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY c.annual_salary)", "currency",
                    RequiredRoles: ["hr_director"])),
            Dimensions: Index(
                new DimensionDefinition("department", "Department", "d.name"),
                new DimensionDefinition("region", "Region", "r.name")),
            Filters: Index(
                new FilterFieldDefinition("department", "Department", "d.name", FieldType.Text),
                new FilterFieldDefinition("region", "Region", "r.name", FieldType.Text)),
            Tables: ["compensation", "employees", "departments", "regions"]),

        new QueryModel(
            Key: "programs",
            Label: "Programs",
            Description: "Internal programme budgets and spend, including D&I.",
            From: "programs pr",
            Metrics: Index(
                new MetricDefinition("program_budget", "Budget", "SUM(pr.budget_amount)", "currency"),
                new MetricDefinition("program_spend", "Spend", "SUM(pr.spend_amount)", "currency"),
                new MetricDefinition("budget_remaining", "Remaining",
                    "SUM(pr.budget_amount - pr.spend_amount)", "currency")),
            Dimensions: Index(
                new DimensionDefinition("program", "Programme", "pr.name"),
                new DimensionDefinition("fiscal_year", "Fiscal year", "pr.fiscal_year::text")),
            Filters: Index(
                new FilterFieldDefinition("program", "Programme", "pr.name", FieldType.Text),
                new FilterFieldDefinition("fiscal_year", "Fiscal year", "pr.fiscal_year", FieldType.Number)),
            Tables: ["programs"]),
    ]);

    // Typed overloads rather than one reflective helper: the key is read from the record directly.
    private static Dictionary<string, MetricDefinition> Index(params MetricDefinition[] items) =>
        items.ToDictionary(i => i.Key, StringComparer.Ordinal);

    private static Dictionary<string, DimensionDefinition> Index(params DimensionDefinition[] items) =>
        items.ToDictionary(i => i.Key, StringComparer.Ordinal);

    private static Dictionary<string, FilterFieldDefinition> Index(params FilterFieldDefinition[] items) =>
        items.ToDictionary(i => i.Key, StringComparer.Ordinal);
}

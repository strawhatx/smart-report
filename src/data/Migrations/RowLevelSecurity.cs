using System.Text;

namespace SmartReport.Data.Migrations;

/// <summary>
/// Generates the row-level security SQL applied alongside the initial schema (D1).
///
/// <para>
/// Three things here are load-bearing and easy to get wrong:
/// </para>
/// <list type="number">
/// <item><c>FORCE ROW LEVEL SECURITY</c> — without it the table owner bypasses every policy, and
/// migrations run as the owner.</item>
/// <item>Every predicate begins by requiring <c>app.role</c> to be set. <c>current_setting(…, true)</c>
/// returns NULL when unset, and a predicate that forgets this grants <b>everything</b> to a
/// connection nobody configured.</item>
/// <item>No write policies on reporting tables. The seeder writes as the owner; the application
/// role gets SELECT only, so an unexpected write is refused by the database rather than by us.</item>
/// </list>
/// </summary>
internal static class RowLevelSecurity
{
    /// <summary>
    /// The non-owner role the application connects as. Must match <c>APP_DB_USER</c> in .env —
    /// <c>RlsCoverageTests</c> fails if the running connection is not this role.
    /// </summary>
    public const string AppRole = "smartreport_app";

    private const string CurrentRole = "current_setting('app.role', true)";
    private const string CurrentRegion = "NULLIF(current_setting('app.region_id', true), '')::int";
    private const string CurrentPrincipal = "NULLIF(current_setting('app.principal', true), '')";

    /// <summary>Readable by any signed-in principal: names of things, not facts about people (D3).</summary>
    private static readonly string[] ReferenceTables =
        ["regions", "departments", "products", "programs"];

    /// <summary>Scoped to the regional manager's own region; other roles see every region.</summary>
    private static readonly string[] RegionScopedTables =
        ["customers", "orders", "order_items", "headcount_monthly", "attrition_events"];

    public static string Up()
    {
        var sql = new StringBuilder();

        foreach (var table in ReferenceTables)
        {
            Enable(sql, table);
            sql.AppendLine($"""
                CREATE POLICY {table}_select ON {table} FOR SELECT TO {AppRole}
                  USING ({CurrentRole} IS NOT NULL);
                """);
            Grant(sql, table, "SELECT");
        }

        foreach (var table in RegionScopedTables)
        {
            Enable(sql, table);
            sql.AppendLine($"""
                CREATE POLICY {table}_select ON {table} FOR SELECT TO {AppRole}
                  USING (
                    {CurrentRole} IS NOT NULL
                    AND (
                      {CurrentRole} <> 'regional_manager'
                      OR region_id = {CurrentRegion}
                    )
                  );
                """);
            Grant(sql, table, "SELECT");
        }

        // Employees is region-scoped like the rest, with one addition: you can always read your
        // own row. That is how the application discovers your role without ever trusting a claim
        // from the cookie (A5) — and without needing an owner connection to bootstrap (D4).
        Enable(sql, "employees");
        sql.AppendLine($"""
            CREATE POLICY employees_select ON employees FOR SELECT TO {AppRole}
              USING (
                principal_key = {CurrentPrincipal}
                OR (
                  {CurrentRole} IS NOT NULL
                  AND (
                    {CurrentRole} <> 'regional_manager'
                    OR region_id = {CurrentRegion}
                  )
                )
              );
            """);
        Grant(sql, "employees", "SELECT");

        // Compensation is the table the demo refuses. PolicyEngine stops an Analyst before a
        // query is compiled; this makes sure that even a bug there returns nothing at all.
        Enable(sql, "compensation");
        sql.AppendLine($"""
            CREATE POLICY compensation_select ON compensation FOR SELECT TO {AppRole}
              USING ({CurrentRole} = 'hr_director');
            """);
        Grant(sql, "compensation", "SELECT");

        // Saved reports are the one thing the application writes, so they get a policy per
        // action (D2). Ownership is checked on both sides of an update so a row cannot be
        // handed to another user.
        Enable(sql, "saved_reports");
        sql.AppendLine($"""
            CREATE POLICY saved_reports_select ON saved_reports FOR SELECT TO {AppRole}
              USING (owner_key = {CurrentPrincipal});

            CREATE POLICY saved_reports_insert ON saved_reports FOR INSERT TO {AppRole}
              WITH CHECK (owner_key = {CurrentPrincipal});

            CREATE POLICY saved_reports_update ON saved_reports FOR UPDATE TO {AppRole}
              USING (owner_key = {CurrentPrincipal})
              WITH CHECK (owner_key = {CurrentPrincipal});

            CREATE POLICY saved_reports_delete ON saved_reports FOR DELETE TO {AppRole}
              USING (owner_key = {CurrentPrincipal});
            """);
        Grant(sql, "saved_reports", "SELECT, INSERT, UPDATE, DELETE");

        return sql.ToString();
    }

    public static string Down()
    {
        var sql = new StringBuilder();
        foreach (var table in AllTables())
        {
            sql.AppendLine($"REVOKE ALL ON {table} FROM {AppRole};");
            sql.AppendLine($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
        }
        return sql.ToString();
    }

    public static IEnumerable<string> AllTables() =>
        ReferenceTables.Concat(RegionScopedTables).Concat(["employees", "compensation", "saved_reports"]);

    private static void Enable(StringBuilder sql, string table) =>
        sql.AppendLine($"""
            ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
            """);

    private static void Grant(StringBuilder sql, string table, string privileges) =>
        sql.AppendLine($"GRANT {privileges} ON {table} TO {AppRole};").AppendLine();
}

using System.Text;

namespace SmartReport.Data.Migrations;

/// <summary>
/// Row-level security, applied in the same migration that creates the tables (D1).
///
/// <para>Four things here are load-bearing and easy to get wrong:</para>
/// <list type="number">
/// <item><c>FORCE ROW LEVEL SECURITY</c> — without it the table owner bypasses every policy, and
/// migrations and the seeder both run as the owner.</item>
/// <item>Every predicate requires <c>app.role</c> to be set. <c>current_setting(…, true)</c>
/// returns NULL when unset and <c>set_config</c> cannot store NULL, so "unset" arrives as an
/// empty string — <c>NULLIF</c> collapses both, and a predicate that skipped it would grant
/// everything to a connection nobody configured.</item>
/// <item><b>The client comes from the session setting, never from the query.</b> Even a query that
/// names another client's id returns nothing, because the policy is applied after the predicate.</item>
/// <item><b>The benchmark cohort floor is repeated here.</b> The compiler already welds it into
/// every query; this makes it hold even if the compiler is wrong.</item>
/// </list>
/// </summary>
internal static class RowLevelSecurity
{
    /// <summary>The non-owner role the application connects as. Must match <c>APP_DB_USER</c>.</summary>
    public const string AppRole = "smartreport_app";

    /// <summary>Mirrors <see cref="Entities.ClientRoles.AccountOwner"/>.</summary>
    private const string AccountOwner = "account_owner";

    /// <summary>A role no client user holds, so Northpoint's own data is unreachable from the portal.</summary>
    private const string NorthpointStaff = "northpoint_staff";

    /// <summary>Minimum clients behind a benchmark row. Mirrors <c>DataCatalog.MinimumCohortSize</c>.</summary>
    private const int MinimumCohortSize = 20;

    private const string CurrentRole = "NULLIF(current_setting('app.role', true), '')";
    private const string CurrentClient = "NULLIF(current_setting('app.client_id', true), '')::int";
    private const string CurrentPrincipal = "NULLIF(current_setting('app.principal', true), '')";

    /// <summary>Scoped to the signed-in client by a <c>client_id</c> column.</summary>
    private static readonly string[] ClientScopedTables =
        ["products", "orders", "order_items", "shipments", "returns"];

    public static string Up()
    {
        var sql = new StringBuilder();

        foreach (var table in ClientScopedTables)
        {
            Enable(sql, table);
            sql.AppendLine($"""
                CREATE POLICY {table}_select ON {table} FOR SELECT TO {AppRole}
                  USING (
                    {CurrentRole} IS NOT NULL
                    AND client_id = {CurrentClient}
                  );
                """);
            Grant(sql, table, "SELECT");
        }

        // The client's own record. Needed for its name; not a window onto the client list.
        Enable(sql, "clients");
        sql.AppendLine($"""
            CREATE POLICY clients_select ON clients FOR SELECT TO {AppRole}
              USING ({CurrentRole} IS NOT NULL AND id = {CurrentClient});
            """);
        Grant(sql, "clients", "SELECT");

        // Colleagues at the same client, plus always your own row — which is how the application
        // discovers your role and client without an owner connection to bootstrap from (D4).
        Enable(sql, "client_users");
        sql.AppendLine($"""
            CREATE POLICY client_users_select ON client_users FOR SELECT TO {AppRole}
              USING (
                principal_key = {CurrentPrincipal}
                OR (
                  {CurrentRole} IS NOT NULL
                  AND client_id = {CurrentClient}
                )
              );
            """);
        Grant(sql, "client_users", "SELECT");

        // Billing: the client's own invoice, and only for whoever owns the account. Two conditions,
        // both required — the role alone must not reach another client's fees.
        Enable(sql, "fees");
        sql.AppendLine($"""
            CREATE POLICY fees_select ON fees FOR SELECT TO {AppRole}
              USING (
                {CurrentRole} = '{AccountOwner}'
                AND client_id = {CurrentClient}
              );
            """);
        Grant(sql, "fees", "SELECT");

        // Northpoint's own commercial data. Same database, no client-facing role can read it.
        Enable(sql, "carrier_contract_rates");
        sql.AppendLine($"""
            CREATE POLICY carrier_contract_rates_select ON carrier_contract_rates FOR SELECT TO {AppRole}
              USING ({CurrentRole} = '{NorthpointStaff}');
            """);
        Grant(sql, "carrier_contract_rates", "SELECT");

        // Benchmarks: anonymized cohort totals, with the minimum-cohort floor repeated here.
        // There is no client_id on this table, so there is nothing to scope and nothing to join
        // back to a client — the only question is whether the cohort is large enough to publish.
        Enable(sql, "benchmark_metrics");
        sql.AppendLine($"""
            CREATE POLICY benchmark_metrics_select ON benchmark_metrics FOR SELECT TO {AppRole}
              USING (
                {CurrentRole} IS NOT NULL
                AND client_count >= {MinimumCohortSize}
              );
            """);
        Grant(sql, "benchmark_metrics", "SELECT");

        // Starter questions. The same list for everyone, so it is reference data (D3).
        Enable(sql, "report_templates");
        sql.AppendLine($"""
            CREATE POLICY report_templates_select ON report_templates FOR SELECT TO {AppRole}
              USING ({CurrentRole} IS NOT NULL);
            """);
        Grant(sql, "report_templates", "SELECT");

        // The only table the application writes, so it gets a policy per action (D2), and
        // ownership is checked on both sides of an update so a row cannot be handed to anyone else.
        Enable(sql, "saved_reports");
        sql.AppendLine($"""
            CREATE POLICY saved_reports_select ON saved_reports FOR SELECT TO {AppRole}
              USING (client_id = {CurrentClient} AND owner_key = {CurrentPrincipal});

            CREATE POLICY saved_reports_insert ON saved_reports FOR INSERT TO {AppRole}
              WITH CHECK (client_id = {CurrentClient} AND owner_key = {CurrentPrincipal});

            CREATE POLICY saved_reports_update ON saved_reports FOR UPDATE TO {AppRole}
              USING (client_id = {CurrentClient} AND owner_key = {CurrentPrincipal})
              WITH CHECK (client_id = {CurrentClient} AND owner_key = {CurrentPrincipal});

            CREATE POLICY saved_reports_delete ON saved_reports FOR DELETE TO {AppRole}
              USING (client_id = {CurrentClient} AND owner_key = {CurrentPrincipal});
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
        ClientScopedTables.Concat([
            "clients", "client_users", "fees", "carrier_contract_rates",
            "benchmark_metrics", "report_templates", "saved_reports",
        ]);

    private static void Enable(StringBuilder sql, string table) =>
        sql.AppendLine($"""
            ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {table} FORCE ROW LEVEL SECURITY;
            """);

    private static void Grant(StringBuilder sql, string table, string privileges) =>
        sql.AppendLine($"GRANT {privileges} ON {table} TO {AppRole};").AppendLine();
}

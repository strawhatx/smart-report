using Npgsql;
using Xunit;

namespace SmartReport.Tests;

/// <summary>
/// The pre-ship check from the Safe Build Pack (D9), as a test rather than a checklist item.
///
/// <para>
/// A new table that nobody remembered to protect fails the build here, instead of being noticed
/// after it has been deployed.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class RlsCoverageTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Every_public_table_has_row_level_security_enabled_and_forced()
    {
        await using var owner = new NpgsqlConnection(fixture.OwnerConnectionString);
        await owner.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT c.relname, c.relrowsecurity, c.relforcerowsecurity
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'r'
              AND n.nspname = 'public'
              AND c.relname <> '__EFMigrationsHistory'
            ORDER BY c.relname
            """, owner);

        var unprotected = new List<string>();
        var checkedCount = 0;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            checkedCount++;
            var (name, enabled, forced) = (reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2));

            // FORCE matters as much as ENABLE: without it the table owner bypasses every policy,
            // and migrations and the seeder both run as the owner.
            if (!enabled) unprotected.Add($"{name} (RLS not enabled)");
            else if (!forced) unprotected.Add($"{name} (RLS not forced)");
        }

        Assert.True(checkedCount > 0, "No tables were found — did the migration run?");
        Assert.True(unprotected.Count == 0,
            "Tables without row-level security: " + string.Join(", ", unprotected));
    }

    [Fact]
    public async Task Every_table_has_at_least_one_select_policy()
    {
        await using var owner = new NpgsqlConnection(fixture.OwnerConnectionString);
        await owner.OpenAsync();

        // RLS with no policy denies everything, which is safe but means the table is unreachable.
        // Either is a mistake worth surfacing.
        await using var command = new NpgsqlCommand(
            """
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'r'
              AND n.nspname = 'public'
              AND c.relname <> '__EFMigrationsHistory'
              AND NOT EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid = c.oid)
            """, owner);

        var missing = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) missing.Add(reader.GetString(0));

        Assert.True(missing.Count == 0, "Tables with RLS but no policy: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task Every_view_runs_as_the_invoker()
    {
        await using var owner = new NpgsqlConnection(fixture.OwnerConnectionString);
        await owner.OpenAsync();

        // A security_definer view over an RLS-protected table silently returns every row (D5).
        // There are no views today; this guards the day someone adds one.
        await using var command = new NpgsqlCommand(
            """
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'v'
              AND n.nspname = 'public'
              AND NOT coalesce((
                    SELECT option_value::bool
                    FROM pg_options_to_table(c.reloptions)
                    WHERE option_name = 'security_invoker'), false)
            """, owner);

        var unsafeViews = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) unsafeViews.Add(reader.GetString(0));

        Assert.True(unsafeViews.Count == 0,
            "Views missing security_invoker = true: " + string.Join(", ", unsafeViews));
    }
}

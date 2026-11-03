using Npgsql;
using SmartReport.Core.Policy;

namespace SmartReport.Data;

/// <summary>
/// Resolves a principal key into role and region, from the database, on every request.
///
/// <para>
/// The cookie proves <i>identity</i>; this supplies <i>authority</i> (A2, A5). Nothing is cached
/// and no role is read from a claim, so a permission change takes effect on the next request
/// rather than at the next sign-in.
/// </para>
/// </summary>
public sealed class PrincipalDirectory(AppDatabase database)
{
    public async Task<Principal?> FindAsync(string principalKey, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = await AppDatabase.BeginAsync(connection, cancellationToken);

        // Phase one: identify only. With app.role unset, the employees policy lets this
        // connection see exactly one row — its own — and nothing else in the database.
        await AppDatabase.ApplyPrincipalAsync(
            connection, transaction, principalKey, role: null, regionId: null, cancellationToken);

        await using var lookup = new NpgsqlCommand(
            """
            SELECT full_name, role, region_id
            FROM employees
            WHERE principal_key = @key AND is_active
            """,
            connection, transaction);
        lookup.Parameters.AddWithValue("key", principalKey);

        string displayName;
        string role;
        int regionId;

        await using (var reader = await lookup.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            displayName = reader.GetString(0);
            role = reader.GetString(1);
            regionId = reader.GetInt32(2);
        }

        // Phase two: now that the role is known, the connection may read reference data.
        await AppDatabase.ApplyPrincipalAsync(
            connection, transaction, principalKey, role, regionId, cancellationToken);

        await using var regionLookup = new NpgsqlCommand(
            "SELECT name FROM regions WHERE id = @id", connection, transaction);
        regionLookup.Parameters.AddWithValue("id", regionId);
        var regionName = await regionLookup.ExecuteScalarAsync(cancellationToken) as string;

        await transaction.RollbackAsync(cancellationToken);

        // Only a regional manager is actually bounded by their region; everyone else sees all of
        // them, so carrying the id further would imply a restriction that is not there.
        return new Principal(
            principalKey,
            displayName,
            role,
            role == Roles.RegionalManager ? regionId : null,
            role == Roles.RegionalManager ? regionName : null);
    }

    /// <summary>
    /// Resolves the demo principals for the development sign-in switcher.
    ///
    /// <para>
    /// Deliberately not a query over <c>employees</c>: with no principal set, row-level security
    /// hides every row, which is correct. Widening the policy so a development convenience could
    /// list people would be weakening a control to make an error go away (C10). Instead this
    /// walks the known demo keys through the ordinary lookup path — so if the switcher works,
    /// the real path works.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<Principal>> ListDemoPrincipalsAsync(CancellationToken cancellationToken)
    {
        var found = new List<Principal>(DemoPrincipals.Keys.Count);

        foreach (var key in DemoPrincipals.Keys)
        {
            if (await FindAsync(key, cancellationToken) is { } principal)
            {
                found.Add(principal);
            }
        }

        return found;
    }
}

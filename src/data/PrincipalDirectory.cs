using Npgsql;
using SmartReport.Core.Policy;

namespace SmartReport.Data;

/// <summary>
/// Resolves a session's principal key into a client, a role, and who to ask for access.
///
/// <para>
/// <b>The client account comes from here, not from the question.</b> The session cookie carries
/// only a key; which client's data is in scope, and what the holder may do with it, are read from
/// the database on every request. Nothing a user types — and nothing the model returns — can
/// change either (A2, A4, A5).
/// </para>
/// </summary>
public sealed class PrincipalDirectory(AppDatabase database)
{
    public async Task<Principal?> FindAsync(string principalKey, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = await AppDatabase.BeginAsync(connection, cancellationToken);

        // Phase one: identify only. With no role set, the policy on client_users lets this
        // connection see exactly one row — its own — and nothing else in the database.
        await AppDatabase.ApplyPrincipalAsync(
            connection, transaction, principalKey, role: null, clientId: null, cancellationToken);

        string displayName, jobTitle, role;
        int clientId;

        await using (var lookup = new NpgsqlCommand(
            """
            SELECT full_name, job_title, role, client_id
            FROM client_users
            WHERE principal_key = @key AND is_active
            """, connection, transaction))
        {
            lookup.Parameters.AddWithValue("key", principalKey);

            await using var reader = await lookup.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            displayName = reader.GetString(0);
            jobTitle = reader.GetString(1);
            role = reader.GetString(2);
            clientId = reader.GetInt32(3);
        }

        // Phase two: with the client and role known, the connection can read its own client's
        // record and find who owns the account.
        await AppDatabase.ApplyPrincipalAsync(
            connection, transaction, principalKey, role, clientId, cancellationToken);

        await using var context = new NpgsqlCommand(
            """
            SELECT c.name,
                   (SELECT u.full_name
                    FROM client_users u
                    WHERE u.client_id = c.id AND u.role = 'account_owner' AND u.is_active
                    ORDER BY u.id
                    LIMIT 1)
            FROM clients c
            WHERE c.id = @id
            """, connection, transaction);
        context.Parameters.AddWithValue("id", clientId);

        string clientName = string.Empty;
        string? accountOwner = null;

        await using (var reader = await context.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                clientName = reader.GetString(0);
                accountOwner = await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1);
            }
        }

        await transaction.RollbackAsync(cancellationToken);

        return new Principal(principalKey, displayName, jobTitle, role, clientId, clientName, accountOwner);
    }

    /// <summary>
    /// Resolves the demo principals for the development sign-in switcher.
    ///
    /// <para>
    /// Deliberately not a query over <c>client_users</c>: with no principal set, row-level
    /// security hides every row, which is correct. Widening a policy so a development convenience
    /// could list people would be weakening a control to make an error go away (C10). This walks
    /// the known keys through the ordinary lookup, so if the switcher works, the real path works.
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

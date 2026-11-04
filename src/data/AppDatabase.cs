using System.Data;
using Npgsql;
using SmartReport.Core.Policy;

namespace SmartReport.Data;

/// <summary>
/// Opens connections as the non-owner application role and applies the principal to them.
///
/// <para>
/// Every read goes through a transaction with <c>SET LOCAL</c>-equivalent settings. This is not
/// a style choice: connections are pooled, and a session-level <c>SET</c> would hand the previous
/// request's identity to whoever is served next (D6). <c>set_config(…, is_local =&gt; true)</c>
/// is scoped to the transaction and disappears when it ends, so a connection always returns to
/// the pool clean.
/// </para>
/// </summary>
public sealed class AppDatabase(string connectionString)
{
    /// <summary>Report queries are interactive; a runaway one should fail rather than hang the page.</summary>
    public const int QueryTimeoutSeconds = 15;

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Writes the principal into the transaction's settings, where the RLS policies read it.
    /// Values are bound as parameters rather than interpolated — they come from our own database,
    /// but the habit is the rule (C6).
    /// </summary>
    public static async Task ApplyPrincipalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string principalKey,
        string? role,
        int? clientId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT set_config('app.principal', @principal, true),
                   set_config('app.role',      @role,      true),
                   set_config('app.client_id', @client,    true)
            """,
            connection, transaction);

        command.Parameters.AddWithValue("principal", principalKey);
        // Empty string rather than NULL: set_config rejects NULL, and the policies treat an empty
        // value as "unset" via NULLIF, which denies.
        command.Parameters.AddWithValue("role", (object?)role ?? string.Empty);
        command.Parameters.AddWithValue("client", clientId?.ToString() ?? string.Empty);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static Task ApplyPrincipalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Principal principal,
        CancellationToken cancellationToken) =>
        ApplyPrincipalAsync(connection, transaction, principal.Key, principal.Role,
            principal.ClientId, cancellationToken);

    public static ValueTask<NpgsqlTransaction> BeginAsync(
        NpgsqlConnection connection, CancellationToken cancellationToken) =>
        connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
}

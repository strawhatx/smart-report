using Npgsql;
using SmartReport.Core.Policy;

namespace SmartReport.Data;

public sealed record SavedReportSummary(
    Guid Id, string Title, string Question, DateTime UpdatedAt);

/// <summary>
/// Reads saved reports as the current principal.
///
/// <para>
/// There is no <c>WHERE owner_key = …</c> anywhere in this file, and that is the point: the
/// policy on <c>saved_reports</c> is owner-only, so the filtering happens in Postgres. If this
/// class were wrong, the database would still only return the caller's rows.
/// </para>
/// </summary>
public sealed class SavedReportStore(AppDatabase database)
{
    public async Task<IReadOnlyList<SavedReportSummary>> ListAsync(
        Principal principal, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = await AppDatabase.BeginAsync(connection, cancellationToken);
        await AppDatabase.ApplyPrincipalAsync(connection, transaction, principal, cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT id, title, question, updated_at
            FROM saved_reports
            ORDER BY updated_at DESC
            LIMIT 50
            """,
            connection, transaction);

        var results = new List<SavedReportSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new SavedReportSummary(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3)));
        }

        return results;
    }

    public async Task<SavedReportSummary?> FindAsync(
        Principal principal, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = await AppDatabase.BeginAsync(connection, cancellationToken);
        await AppDatabase.ApplyPrincipalAsync(connection, transaction, principal, cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT id, title, question, updated_at FROM saved_reports WHERE id = @id",
            connection, transaction);
        command.Parameters.AddWithValue("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new SavedReportSummary(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3))
            : null;
    }
}

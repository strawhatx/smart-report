using Npgsql;
using SmartReport.Core.Policy;

namespace SmartReport.Data;

public sealed record SavedReportSummary(Guid Id, string Title, string Question, DateTime UpdatedAt);

public sealed record ReportTemplateSummary(int Id, string Title, string Question, string Category);

/// <summary>
/// The Library: starter templates everyone gets, and the reports this person saved.
///
/// <para>
/// There is no <c>WHERE client_id = …</c> or <c>WHERE owner_key = …</c> anywhere in this file, and
/// that is the point. The policy on <c>saved_reports</c> scopes to both; if this class were wrong,
/// the database would still return only the caller's own rows.
/// </para>
/// </summary>
public sealed class SavedReportStore(AppDatabase database)
{
    public async Task<IReadOnlyList<ReportTemplateSummary>> ListTemplatesAsync(
        Principal principal, CancellationToken cancellationToken)
    {
        var results = new List<ReportTemplateSummary>();

        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = await AppDatabase.BeginAsync(connection, cancellationToken);
        await AppDatabase.ApplyPrincipalAsync(connection, transaction, principal, cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT id, title, question, category FROM report_templates ORDER BY sort_order",
            connection, transaction);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new ReportTemplateSummary(
                reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return results;
    }

    public async Task<IReadOnlyList<SavedReportSummary>> ListSavedAsync(
        Principal principal, CancellationToken cancellationToken)
    {
        var results = new List<SavedReportSummary>();

        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = await AppDatabase.BeginAsync(connection, cancellationToken);
        await AppDatabase.ApplyPrincipalAsync(connection, transaction, principal, cancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT id, title, question, updated_at FROM saved_reports ORDER BY updated_at DESC LIMIT 50",
            connection, transaction);

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

    /// <summary>
    /// Saves a report for the current principal.
    ///
    /// <para>
    /// The owner and client are taken from the resolved principal, never from the request (A4),
    /// and the policy's <c>WITH CHECK</c> rejects the insert if they disagree — so a bug here
    /// fails loudly rather than filing a row under someone else's name.
    /// </para>
    /// </summary>
    public async Task<Guid> SaveAsync(
        Principal principal, string title, string question, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = await AppDatabase.BeginAsync(connection, cancellationToken);
        await AppDatabase.ApplyPrincipalAsync(connection, transaction, principal, cancellationToken);

        var id = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO saved_reports (id, client_id, owner_key, title, question, created_at, updated_at)
            VALUES (@id, @client, @owner, @title, @question, @now, @now)
            """, connection, transaction);

        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("client", principal.ClientId);
        command.Parameters.AddWithValue("owner", principal.Key);
        command.Parameters.AddWithValue("title", title);
        command.Parameters.AddWithValue("question", question);
        command.Parameters.AddWithValue("now", now);

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return id;
    }
}

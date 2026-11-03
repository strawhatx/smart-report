using Npgsql;
using SmartReport.Core.Abstractions;
using SmartReport.Core.Policy;
using SmartReport.Core.Sql;

namespace SmartReport.Data;

/// <summary>
/// Runs a compiled query as the given principal.
///
/// <para>
/// The transaction here is not for atomicity — these are reads — but for scoping. The principal's
/// settings live and die with it (D6), and the rollback at the end is simply the cheapest way to
/// close a read.
/// </para>
/// </summary>
public sealed class ReportQueryRunner(AppDatabase database) : IReportQueryRunner
{
    public async Task<QueryResult> RunAsync(
        CompiledQuery query, Principal principal, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        await using var transaction = await AppDatabase.BeginAsync(connection, cancellationToken);

        await AppDatabase.ApplyPrincipalAsync(connection, transaction, principal, cancellationToken);

        await using var command = new NpgsqlCommand(query.Sql, connection, transaction)
        {
            CommandTimeout = AppDatabase.QueryTimeoutSeconds,
        };

        foreach (var parameter in query.Parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        var rows = new List<object?[]>();

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new object?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = await reader.IsDBNullAsync(i, cancellationToken)
                        ? null
                        : reader.GetValue(i);
                }
                rows.Add(row);
            }
        }

        await transaction.RollbackAsync(cancellationToken);

        return new QueryResult(query.Columns, rows);
    }
}

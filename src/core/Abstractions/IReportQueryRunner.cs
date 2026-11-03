using SmartReport.Core.Policy;
using SmartReport.Core.Sql;

namespace SmartReport.Core.Abstractions;

public sealed record QueryResult(IReadOnlyList<ResultColumn> Columns, IReadOnlyList<object?[]> Rows);

/// <summary>
/// Executes a compiled query as the given principal.
///
/// <para>
/// Implementations must open a transaction, apply the principal's <c>app.*</c> settings with
/// <c>SET LOCAL</c>, and run the query inside it — see rule D6. Row-level security is what
/// actually bounds the rows; this interface exists so <c>core</c> can describe that without
/// depending on a database.
/// </para>
/// </summary>
public interface IReportQueryRunner
{
    Task<QueryResult> RunAsync(
        CompiledQuery query, Principal principal, CancellationToken cancellationToken);
}

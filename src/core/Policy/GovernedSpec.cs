using SmartReport.Core.Specs;

namespace SmartReport.Core.Policy;

/// <summary>A query that survived authorization, with its effective row cap.</summary>
public sealed record GovernedQuery(SpecQuery Query, int RowLimit);

/// <summary>
/// The outcome of authorizing a validated spec for one principal. Only
/// <see cref="Queries"/> is ever compiled.
/// </summary>
public sealed record GovernedSpec(
    string Title,
    IReadOnlyList<GovernedQuery> Queries,
    IReadOnlyList<PolicyDecision> Decisions,
    IReadOnlyList<string> TablesRead,
    IReadOnlyList<string> Withheld)
{
    /// <summary>True when nothing survived — the user gets an explanation, not an empty chart.</summary>
    public bool IsRefused => Queries.Count == 0;

    /// <summary>Why the report was refused, in plain language. Empty when it wasn't.</summary>
    public string RefusalReason => IsRefused
        ? string.Join(" ", Decisions.Where(d => d.Outcome == PolicyOutcome.Denied).Select(d => d.Reason))
        : string.Empty;
}

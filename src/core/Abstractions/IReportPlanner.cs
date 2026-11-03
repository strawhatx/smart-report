using SmartReport.Core.Policy;
using SmartReport.Core.Specs;

namespace SmartReport.Core.Abstractions;

/// <summary>Something the planner emitted on its way to an answer.</summary>
public abstract record PlannerEvent
{
    /// <summary>Narration for the conversation rail. Display only — never acted on.</summary>
    public sealed record Thinking(string Text) : PlannerEvent;

    /// <summary>The structured request. Still untrusted at this point: the validator runs next.</summary>
    public sealed record Planned(ReportSpec Spec) : PlannerEvent;

    /// <summary>
    /// Nothing in the catalog fits the question. This is a first-class outcome, not a failure —
    /// an honest "I can't answer that yet" beats an improvised query.
    /// </summary>
    public sealed record NoMatch(string Message) : PlannerEvent;
}

/// <summary>
/// Turns a question into a <see cref="ReportSpec"/>.
///
/// <para>
/// Implementations are given the catalog's description and nothing else: no connection, no
/// schema, no ability to execute anything. Whatever they return is validated before use, so a
/// compromised or simply bad model cannot widen what the user can reach.
/// </para>
/// </summary>
public interface IReportPlanner
{
    /// <summary>Shown in the UI so it is always clear which engine answered.</summary>
    string ProviderName { get; }

    IAsyncEnumerable<PlannerEvent> PlanAsync(
        string question, Principal principal, CancellationToken cancellationToken);
}

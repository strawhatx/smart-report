namespace SmartReport.Core.Abstractions;

/// <summary>
/// A question, and what the reader was looking at when they asked it.
///
/// <para>
/// People do not restate the whole report to change it. They say "convert this to a table", "and
/// by channel", "now just Shopify" — all of which mean nothing without the previous turn. The
/// planner is given that turn so it can return a modified plan rather than guess.
/// </para>
/// <para>
/// <b>The context is untrusted and it does not matter.</b> It arrives from the browser and could
/// say anything; whatever the planner returns from it is validated against the catalog and
/// authorized for the principal exactly as a first question would be. It steers the planner; it
/// cannot widen what comes back.
/// </para>
/// </summary>
/// <param name="PreviousPlanJson">The plan behind the report on screen, as the client received it.</param>
public sealed record PlanRequest(
    string Question,
    string? PreviousQuestion = null,
    string? PreviousPlanJson = null)
{
    public bool IsFollowUp => !string.IsNullOrWhiteSpace(PreviousPlanJson);
}

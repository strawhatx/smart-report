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
/// <param name="ValidationProblems">
/// Set only on a retry, to tell the planner what was wrong with its last attempt.
///
/// <para>
/// The catalog is not uniform — a date range is <c>shipped_at</c> on one model and
/// <c>received_at</c> on another — and the schema offers the union of every filter name, so
/// using one on the wrong model is allowed by the schema and caught by the validator. Handing
/// those errors back is cheaper than refusing a reasonable question over a naming detail.
/// </para>
/// </summary>
public sealed record PlanRequest(
    string Question,
    string? PreviousQuestion = null,
    string? PreviousPlanJson = null,
    string? ValidationProblems = null)
{
    public bool IsFollowUp => !string.IsNullOrWhiteSpace(PreviousPlanJson);

    public bool IsRetry => !string.IsNullOrWhiteSpace(ValidationProblems);
}

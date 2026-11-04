using SmartReport.Core.Policy;
using SmartReport.Core.Specs;

namespace SmartReport.Core.Reporting;

/// <summary>
/// What the browser is told, in the order it happens. The event names are the SSE event names.
/// </summary>
public abstract record ReportEvent(string Name)
{
    public sealed record Thinking(string Text) : ReportEvent("thinking");

    /// <summary>The validated plan, as chips: what it decided to ask for.</summary>
    public sealed record Planned(ReportSpec Spec) : ReportEvent("spec");

    /// <summary>
    /// One authorization decision. These are the point of the whole demo, so they are streamed
    /// individually as they are made rather than summarised at the end.
    /// </summary>
    public sealed record Guard(PolicyDecision Decision) : ReportEvent("guard");

    public sealed record Sql(string Model, string Text) : ReportEvent("sql");

    public sealed record Rows(string Model, int Count) : ReportEvent("rows");

    public sealed record Document(ReportDocument Report) : ReportEvent("document");

    /// <summary>
    /// What the report says, written by the model from the figures it returned.
    ///
    /// <para>
    /// Sent after the document, so the report renders whether or not narration arrives — and so
    /// a slow or unavailable narrator costs the reader nothing but prose.
    /// </para>
    /// </summary>
    public sealed record Findings(
        IReadOnlyList<Abstractions.Finding> Items, string Disclosure) : ReportEvent("findings");

    /// <summary>
    /// The question was understood and deliberately not answered.
    /// </summary>
    /// <param name="Remedy">
    /// What the asker can do about it, where there is something — a named person to request
    /// access from, or a measure they are already allowed. Carried here as well as on the
    /// decision, because the refusal panel is where the reader is looking.
    /// </param>
    public sealed record Refused(string Message, string? Remedy = null) : ReportEvent("refused");

    /// <summary>Something went wrong. Carries a safe message only (C5).</summary>
    public sealed record Failed(string Message) : ReportEvent("failed");

    public sealed record Done() : ReportEvent("done");
}

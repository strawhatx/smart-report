namespace SmartReport.Core.Policy;

public enum PolicyOutcome
{
    Allowed,

    /// <summary>Refused, with a reason and — where one exists — somewhere to go next.</summary>
    Denied,

    /// <summary>Allowed, but bounded. Row scoping to the signed-in client.</summary>
    Scoped,

    /// <summary>
    /// Allowed only in aggregate. A disclosure safeguard applied — the minimum-cohort floor on
    /// benchmarks — so the figure cannot be unpicked into the individuals behind it.
    /// </summary>
    Aggregated,
}

/// <summary>
/// One authorization decision, streamed to the UI as it is made.
///
/// <para>
/// These exist because row-level security alone is illegible: it returns fewer rows without saying
/// why, and cannot distinguish "there were none" from "you may not see them". A decision carries a
/// reason, and a denial carries a <see cref="Remedy"/> where there is one.
/// </para>
/// </summary>
/// <param name="Subject">What was decided about, e.g. <c>metric:total_fees</c>.</param>
/// <param name="Remedy">
/// What the asker can actually do — a named person to request access from, or a measure they are
/// already allowed. Only ever set to something the engine has checked they may use.
/// </param>
public sealed record PolicyDecision(
    PolicyOutcome Outcome,
    string Subject,
    string Reason,
    string? Remedy = null)
{
    public static PolicyDecision Allow(string subject, string reason) =>
        new(PolicyOutcome.Allowed, subject, reason);

    public static PolicyDecision Deny(string subject, string reason, string? remedy = null) =>
        new(PolicyOutcome.Denied, subject, reason, remedy);

    public static PolicyDecision Scope(string subject, string reason) =>
        new(PolicyOutcome.Scoped, subject, reason);

    public static PolicyDecision Aggregate(string subject, string reason) =>
        new(PolicyOutcome.Aggregated, subject, reason);
}

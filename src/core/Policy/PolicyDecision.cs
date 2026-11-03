namespace SmartReport.Core.Policy;

public enum PolicyOutcome
{
    Allowed,
    Denied,
    Scoped,
}

/// <summary>
/// One authorization decision, streamed to the UI as it is made.
///
/// <para>
/// These exist because row-level security alone is illegible: it returns fewer rows without
/// saying why, and it cannot distinguish "there were none" from "you may not see them". A
/// <see cref="PolicyOutcome.Denied"/> decision carries a reason the user can act on.
/// </para>
/// </summary>
/// <param name="Subject">What was decided about, e.g. <c>metric:avg_salary</c>.</param>
public sealed record PolicyDecision(PolicyOutcome Outcome, string Subject, string Reason)
{
    public static PolicyDecision Allow(string subject, string reason) =>
        new(PolicyOutcome.Allowed, subject, reason);

    public static PolicyDecision Deny(string subject, string reason) =>
        new(PolicyOutcome.Denied, subject, reason);

    public static PolicyDecision Scope(string subject, string reason) =>
        new(PolicyOutcome.Scoped, subject, reason);
}

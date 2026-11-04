namespace SmartReport.Data;

/// <summary>
/// The two seeded identities the demo can sign in as — both at Trailhead Coffee Co.
///
/// <para>
/// They contrast <i>roles</i>, not companies. A cross-company switch proves only what every
/// reviewer already assumes; a coordinator being refused the invoice, and told who can grant it,
/// shows a rule with a reason and a way forward.
/// </para>
/// </summary>
public static class DemoPrincipals
{
    /// <summary>Dana Whitfield. Operations, but not billing.</summary>
    public const string Coordinator = "dana";

    /// <summary>Priya Shah. Owns the account, so the invoice is hers.</summary>
    public const string AccountOwner = "priya";

    public static readonly IReadOnlyList<string> Keys = [Coordinator, AccountOwner];
}

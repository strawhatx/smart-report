namespace SmartReport.App.Pages;

/// <summary>
/// Labels for the development sign-in switcher.
///
/// <para>
/// Display text only — selecting one of these grants nothing. The chosen key still has to resolve
/// through <c>PrincipalDirectory</c>, and the role still comes from that employee's row (A5).
/// </para>
/// </summary>
public static class DemoPrincipalList
{
    public sealed record Demo(string Key, string Label);

    public static readonly IReadOnlyList<Demo> All =
    [
        new(SmartReport.Data.DemoPrincipals.Analyst, "Dana · Analyst"),
        new(SmartReport.Data.DemoPrincipals.RegionalManager, "Marcus · Regional Mgr"),
        new(SmartReport.Data.DemoPrincipals.HrDirector, "Priya · HR Director"),
    ];
}

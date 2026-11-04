namespace SmartReport.App.Pages;

/// <summary>
/// The two identities the portal can sign in as.
///
/// <para>
/// Display text only — picking one grants nothing. The key still has to resolve through
/// <c>PrincipalDirectory</c>, and the role still comes from that user's row (A5).
/// </para>
/// </summary>
public static class DemoPrincipalList
{
    public sealed record Demo(string Key, string Name, string Role);

    public static readonly IReadOnlyList<Demo> All =
    [
        new(SmartReport.Data.DemoPrincipals.Coordinator, "Dana Whitfield", "Warehouse Coordinator"),
        new(SmartReport.Data.DemoPrincipals.AccountOwner, "Priya Shah", "Account Owner"),
    ];
}

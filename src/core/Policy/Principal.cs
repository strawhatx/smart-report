namespace SmartReport.Core.Policy;

/// <summary>Role keys. These match the values written into <c>app.role</c> for Postgres RLS.</summary>
public static class Roles
{
    public const string Analyst = "analyst";
    public const string RegionalManager = "regional_manager";
    public const string HrDirector = "hr_director";

    public static readonly IReadOnlyList<string> All = [Analyst, RegionalManager, HrDirector];

    public static string Label(string role) => role switch
    {
        Analyst => "Analyst",
        RegionalManager => "Regional Manager",
        HrDirector => "HR Director",
        _ => role,
    };
}

/// <summary>
/// Who is asking.
///
/// <para>
/// Built by the app from the database on every request, never from a request field and never from
/// a claim baked into the cookie at sign-in (A2, A4, A5). The cookie says who you are; this says
/// what you may do, and it is re-read each time so a permission change takes effect immediately.
/// </para>
/// </summary>
public sealed record Principal(
    string Key,
    string DisplayName,
    string Role,
    int? RegionId,
    string? RegionName)
{
    public bool IsRegionScoped => Role == Roles.RegionalManager;
}

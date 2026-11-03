namespace SmartReport.Data;

/// <summary>
/// The seeded demo identities.
///
/// <para>
/// These exist so the development sign-in switcher has something to offer and so the test suite
/// can assert on three concretely different permission shapes. They are demo data, not an access
/// control list — authority still comes from each employee's row (A5).
/// </para>
/// </summary>
public static class DemoPrincipals
{
    public const string Analyst = "dana";
    public const string RegionalManager = "marcus";
    public const string HrDirector = "priya";

    public static readonly IReadOnlyList<string> Keys = [Analyst, RegionalManager, HrDirector];
}

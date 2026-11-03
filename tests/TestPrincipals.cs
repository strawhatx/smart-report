using SmartReport.Core.Catalog;
using SmartReport.Core.Policy;

namespace SmartReport.Tests;

/// <summary>The three demo principals, matching the seeded data.</summary>
internal static class TestPrincipals
{
    public static readonly Principal Dana =
        new("dana", "Dana Okafor", Roles.Analyst, RegionId: null, RegionName: null);

    public static readonly Principal Marcus =
        new("marcus", "Marcus Lindqvist", Roles.RegionalManager, RegionId: 1, RegionName: "North");

    public static readonly Principal Priya =
        new("priya", "Priya Raman", Roles.HrDirector, RegionId: null, RegionName: null);

    public static DataCatalog Catalog => DataCatalog.CreateDefault();
}

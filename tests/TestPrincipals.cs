using SmartReport.Core.Catalog;
using SmartReport.Core.Policy;

namespace SmartReport.Tests;

/// <summary>The seeded identities, plus one at another client the UI can't reach.</summary>
internal static class TestPrincipals
{
    public const int TrailheadId = 1;
    public const int BluefinId = 2;

    /// <summary>Operations, but not billing. The one the demo signs in as first.</summary>
    public static readonly Principal Dana = new(
        "dana", "Dana Whitfield", "Warehouse Coordinator", Roles.Coordinator,
        TrailheadId, "Trailhead Coffee Co.", "Priya Shah");

    /// <summary>Owns the account, so the invoice is hers.</summary>
    public static readonly Principal Priya = new(
        "priya", "Priya Shah", "Account Owner", Roles.AccountOwner,
        TrailheadId, "Trailhead Coffee Co.", "Priya Shah");

    /// <summary>A different client entirely. Not signable-in; exists so isolation has a subject.</summary>
    public static readonly Principal Sam = new(
        "sam", "Sam Oyelaran", "Account Owner", Roles.AccountOwner,
        BluefinId, "Bluefin Apparel", "Sam Oyelaran");

    public static DataCatalog Catalog => DataCatalog.CreateDefault();
}

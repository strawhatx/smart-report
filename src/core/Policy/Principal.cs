namespace SmartReport.Core.Policy;

/// <summary>Roles a client's own people hold. Must match the values written into <c>app.role</c>.</summary>
public static class Roles
{
    public const string Coordinator = "coordinator";
    public const string AccountOwner = "account_owner";

    public static string Label(string role) => role switch
    {
        Coordinator => "Warehouse Coordinator",
        AccountOwner => "Account Owner",
        "northpoint_staff" => "Northpoint staff",
        _ => role,
    };
}

/// <summary>
/// Who is asking, and on whose behalf.
///
/// <para>
/// Built from the database on every request. <b>The client account comes from the signed-in
/// session, never from the model's prompt</b> — nothing a question says can change which client's
/// data is in scope. Role is read from the user's row rather than a cookie claim, so a permission
/// change takes effect on the next request (A2, A4, A5).
/// </para>
/// </summary>
/// <param name="AccountOwnerName">
/// Who to ask for access. A refusal that names a person is actionable; one that says "denied" is not.
/// </param>
public sealed record Principal(
    string Key,
    string DisplayName,
    string JobTitle,
    string Role,
    int ClientId,
    string ClientName,
    string? AccountOwnerName)
{
    public bool IsAccountOwner => Role == Roles.AccountOwner;
}

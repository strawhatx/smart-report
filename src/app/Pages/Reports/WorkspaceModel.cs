using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartReport.App.Security;
using SmartReport.Core.Policy;

namespace SmartReport.App.Pages.Reports;

/// <summary>Shared state for the two workspace routes: a new report, and a saved one reopened.</summary>
public abstract class WorkspaceModel(PrincipalContext principals) : PageModel
{
    public Principal? Principal { get; private set; }
    public string Title { get; protected set; } = "New report";
    public string Question { get; protected set; } = string.Empty;

    protected async Task<Principal?> LoadAsync(CancellationToken cancellationToken)
    {
        // C3: the check lives in the handler, beside the data it protects.
        Principal = await principals.GetAsync(cancellationToken);
        ViewData["Principal"] = Principal;
        ViewData["PageKey"] = "reports";

        // A10: nothing about one person's workspace may be cached for anyone else.
        Response.Headers.CacheControl = "no-store";

        return Principal;
    }
}

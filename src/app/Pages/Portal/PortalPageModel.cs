using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartReport.App.Security;
using SmartReport.Core.Policy;

namespace SmartReport.App.Pages.Portal;

/// <summary>Shared behaviour for the two portal views.</summary>
public abstract class PortalPageModel(PrincipalContext principals) : PageModel
{
    public Principal? Principal { get; private set; }

    protected async Task LoadAsync(string tab, CancellationToken cancellationToken)
    {
        // C3: the check lives in the handler, beside the data it protects.
        Principal = await principals.GetAsync(cancellationToken);
        ViewData["Principal"] = Principal;
        ViewData["Tab"] = tab;

        // A10: one client's workspace must never be cached for anyone else.
        Response.Headers.CacheControl = "no-store";
    }
}

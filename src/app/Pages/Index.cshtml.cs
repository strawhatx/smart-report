using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartReport.App.Security;

namespace SmartReport.App.Pages;

/// <summary>
/// The landing page. Public and cacheable — it reads the principal only to show who is signed
/// in, never to decide what to render.
/// </summary>
public sealed class IndexModel(PrincipalContext principals) : PageModel
{
    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        ViewData["Principal"] = await principals.GetAsync(cancellationToken);
}

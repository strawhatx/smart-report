using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartReport.App.Security;
using SmartReport.Core.Policy;
using SmartReport.Data;

namespace SmartReport.App.Pages.Reports;

public sealed class IndexModel(PrincipalContext principals, SavedReportStore store) : PageModel
{
    public Principal? Principal { get; private set; }
    public IReadOnlyList<SavedReportSummary> Reports { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        // Checked inside the handler, not only by routing (C3). This page degrades to an empty
        // state rather than redirecting, because the dev banner is how you sign in.
        Principal = await principals.GetAsync(cancellationToken);
        ViewData["Principal"] = Principal;

        // Per-user content: never cached anywhere shared (A10).
        Response.Headers.CacheControl = "no-store";

        if (Principal is not null)
        {
            Reports = await store.ListAsync(Principal, cancellationToken);
        }
    }
}

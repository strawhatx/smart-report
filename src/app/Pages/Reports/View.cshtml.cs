using Microsoft.AspNetCore.Mvc;
using SmartReport.App.Security;
using SmartReport.Data;

namespace SmartReport.App.Pages.Reports;

public sealed class ViewModel(PrincipalContext principals, SavedReportStore store)
    : WorkspaceModel(principals)
{
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var principal = await LoadAsync(cancellationToken);
        if (principal is null)
        {
            return Page();
        }

        // No ownership check here on purpose: the policy on saved_reports is owner-only, so
        // someone else's id simply doesn't resolve. One mechanism, enforced in one place (D2).
        var report = await store.FindAsync(principal, id, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        Title = report.Title;
        Question = report.Question;
        return Page();
    }
}

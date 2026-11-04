using Microsoft.AspNetCore.Mvc;
using SmartReport.App.Security;
using SmartReport.Data;

namespace SmartReport.App.Pages.Portal;

/// <summary>
/// The Smart Reports workspace.
///
/// <para>
/// The page is a shell: the report itself is streamed from <c>/api/reports/ask</c> so the plan,
/// the authorization decisions and the compiled SQL arrive as they happen rather than appearing
/// all at once at the end.
/// </para>
/// </summary>
public sealed class ReportsModel(PrincipalContext principals, SavedReportStore store)
    : PortalPageModel(principals)
{
    public string Title { get; private set; } = "New report";

    /// <summary>Pre-filled when a template or a saved report was opened. Asked automatically.</summary>
    public string Question { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(
        Guid? saved, int? template, CancellationToken cancellationToken)
    {
        await LoadAsync("report", cancellationToken);

        if (Principal is null)
        {
            return Page();
        }

        if (saved is { } savedId)
        {
            // No ownership check here on purpose: the policy on saved_reports scopes to both the
            // client and the person, so someone else's id simply does not resolve (D2).
            var report = await store.FindAsync(Principal, savedId, cancellationToken);
            if (report is null)
            {
                return NotFound();
            }

            Title = report.Title;
            Question = report.Question;
        }
        else if (template is { } templateId)
        {
            var templates = await store.ListTemplatesAsync(Principal, cancellationToken);
            var match = templates.FirstOrDefault(t => t.Id == templateId);
            if (match is null)
            {
                return NotFound();
            }

            Title = match.Title;
            Question = match.Question;
        }

        return Page();
    }
}

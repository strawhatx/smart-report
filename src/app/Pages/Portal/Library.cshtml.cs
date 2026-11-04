using SmartReport.App.Security;
using SmartReport.Data;

namespace SmartReport.App.Pages.Portal;

/// <summary>
/// The Library: starter questions everyone gets, and the reports this person saved.
///
/// <para>
/// Templates are global reference data. Saved reports are scoped by the database to the client
/// <i>and</i> the person — note there is no filtering in this class or its store (D2).
/// </para>
/// </summary>
public sealed class LibraryModel(PrincipalContext principals, SavedReportStore store)
    : PortalPageModel(principals)
{
    public IReadOnlyList<ReportTemplateSummary> Templates { get; private set; } = [];
    public IReadOnlyList<SavedReportSummary> Saved { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync("library", cancellationToken);

        if (Principal is null)
        {
            return;
        }

        Templates = await store.ListTemplatesAsync(Principal, cancellationToken);
        Saved = await store.ListSavedAsync(Principal, cancellationToken);
    }
}

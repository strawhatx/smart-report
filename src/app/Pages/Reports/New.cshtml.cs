using SmartReport.App.Security;

namespace SmartReport.App.Pages.Reports;

public sealed class NewModel(PrincipalContext principals) : WorkspaceModel(principals)
{
    public async Task OnGetAsync(string? q, CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);

        // A starting question may be passed in the query string. It is only ever echoed into a
        // textarea — Razor encodes it — and it is validated server-side when it is submitted (C4).
        Question = string.IsNullOrWhiteSpace(q) ? string.Empty : q.Trim();
        Title = "New report";
    }
}

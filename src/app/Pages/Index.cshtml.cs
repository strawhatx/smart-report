using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartReport.App.Pages;

/// <summary>
/// Northpoint's marketing page: the door into the portal.
///
/// <para>
/// Public and cacheable. It reads no principal and no data — the only live thing on it is the
/// link to Smart Reports.
/// </para>
/// </summary>
public sealed class IndexModel : PageModel
{
    public void OnGet()
    {
    }
}

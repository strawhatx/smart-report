using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SmartReport.App.Pages;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
[IgnoreAntiforgeryToken]
/// <summary>
/// The page an unhandled exception lands on outside development.
///
/// <para>
/// It shows a request id and nothing else — no message, no stack trace, no database detail
/// (C5). The id is the link between what the user can tell you and what the server log holds,
/// which is the whole point of showing it.
/// </para>
/// </summary>
public class ErrorModel : PageModel
{
    public string? RequestId { get; set; }

    public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

    public void OnGet()
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }
}


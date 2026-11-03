using System.Security.Claims;
using SmartReport.Core.Policy;
using SmartReport.Data;

namespace SmartReport.App.Security;

/// <summary>
/// Resolves the current <see cref="Principal"/> for this request.
///
/// <para>
/// The cookie supplies one thing only: a principal key. Role and region are read from the
/// database every request (A2, A5), so a tampered or stale cookie cannot widen access — the worst
/// it can do is name someone who doesn't exist, which resolves to nobody.
/// </para>
/// <para>
/// Cached per request, not across requests: a permission change takes effect on the next page
/// load rather than at the next sign-in.
/// </para>
/// </summary>
public sealed class PrincipalContext(
    IHttpContextAccessor accessor,
    PrincipalDirectory directory)
{
    public const string PrincipalKeyClaim = "smartreport:principal";

    private Principal? _cached;
    private bool _resolved;

    public async Task<Principal?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_resolved)
        {
            return _cached;
        }

        _resolved = true;

        var key = accessor.HttpContext?.User.FindFirstValue(PrincipalKeyClaim);
        if (!string.IsNullOrWhiteSpace(key))
        {
            _cached = await directory.FindAsync(key, cancellationToken);
        }

        return _cached;
    }
}

public static class PrincipalContextExtensions
{
    /// <summary>
    /// Resolves the principal or fails the request.
    ///
    /// <para>
    /// Called <i>inside</i> every handler that touches private data, not only as an attribute on
    /// the page (C3). A Razor Page handler is a public endpoint — <c>OnPostDeleteAsync</c> is a
    /// POST to <c>/page?handler=Delete</c> — so the check belongs next to the data.
    /// </para>
    /// </summary>
    public static async Task<Principal> RequireUserAsync(
        this PrincipalContext context, CancellationToken cancellationToken = default) =>
        await context.GetAsync(cancellationToken)
        ?? throw new NotSignedInException();
}

/// <summary>Thrown when a handler needs a principal and there isn't one. Mapped to 401 or a redirect.</summary>
public sealed class NotSignedInException() : Exception("Not signed in.");

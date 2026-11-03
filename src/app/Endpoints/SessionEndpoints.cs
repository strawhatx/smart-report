using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SmartReport.App.Security;
using SmartReport.Data;

namespace SmartReport.App.Endpoints;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this WebApplication app)
    {
        // --- The development sign-in switcher. See rule A12.
        //
        // This endpoint hands out any seeded identity with no password. It exists so the demo can
        // show the same question answered as three different people. It returns 404 outside
        // development, and the layout shows a banner whenever it is reachable, so it cannot
        // quietly survive into somewhere it shouldn't.
        app.MapPost("/dev/signin-as/{key}", async (
            string key,
            HttpContext context,
            PrincipalDirectory directory,
            IHostEnvironment environment,
            CancellationToken cancellationToken) =>
        {
            if (!environment.IsDevelopment())
            {
                return Results.NotFound();
            }

            // Even here the identity must exist and be resolvable through the ordinary path.
            var principal = await directory.FindAsync(key, cancellationToken);
            if (principal is null)
            {
                return Results.NotFound();
            }

            // The cookie carries the key only. Role and region are read from the database on
            // every request, so nothing here grants authority (A5).
            var identity = new ClaimsIdentity(
                [
                    new Claim(PrincipalContext.PrincipalKeyClaim, principal.Key),
                    new Claim(ClaimTypes.Name, principal.DisplayName),
                ],
                CookieAuthenticationDefaults.AuthenticationScheme);

            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return Results.LocalRedirect(SafeReturn(context));
        });

        app.MapPost("/signout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("/");
        });
    }

    /// <summary>
    /// Only ever redirects within this site (A7). Anything else — including a protocol-relative
    /// URL, which <c>startsWith("/")</c> alone would wave through — goes to the reports page.
    /// </summary>
    private static string SafeReturn(HttpContext context)
    {
        var requested = context.Request.Form["returnUrl"].ToString();

        return !string.IsNullOrEmpty(requested)
               && Uri.IsWellFormedUriString(requested, UriKind.Relative)
               && requested.StartsWith('/')
               && !requested.StartsWith("//")
               && !requested.StartsWith("/\\")
            ? requested
            : "/reports";
    }
}

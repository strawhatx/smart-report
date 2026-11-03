using System.Security.Cryptography;

namespace SmartReport.App.Security;

/// <summary>
/// Sets the response headers from rule C8, including a Content Security Policy with no
/// <c>unsafe-inline</c>.
///
/// <para>
/// That policy is the reason GSAP, Lenis and Three.js are vendored into <c>wwwroot/lib/</c>
/// rather than pulled from a CDN: a policy that allows arbitrary third-party script origins is
/// not worth setting. Inline scripts and styles carry a per-request nonce instead.
/// </para>
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    public const string NonceItemKey = "csp-nonce";

    public async Task InvokeAsync(HttpContext context)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Items[NonceItemKey] = nonce;

        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["X-Frame-Options"] = "DENY";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";

        // 'self' only: every script and style is served from this origin.
        var csp =
            "default-src 'self'; " +
            $"script-src 'self' 'nonce-{nonce}'; " +
            $"style-src 'self' 'nonce-{nonce}'; " +
            "img-src 'self' data:; " +
            "font-src 'self'; " +
            "connect-src 'self'; " +
            "object-src 'none'; " +
            "base-uri 'none'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'";

        // In development the browser-refresh script is injected inline without our nonce, so the
        // policy is reported rather than enforced. Production always enforces.
        headers[environment.IsDevelopment() ? "Content-Security-Policy-Report-Only" : "Content-Security-Policy"] = csp;

        await next(context);
    }
}

public static class SecurityHeaderExtensions
{
    public static IApplicationBuilder UseSmartReportSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();

    /// <summary>The nonce for this request, for inline <c>&lt;script&gt;</c> and <c>&lt;style&gt;</c>.</summary>
    public static string CspNonce(this HttpContext context) =>
        context.Items[SecurityHeadersMiddleware.NonceItemKey] as string ?? string.Empty;
}

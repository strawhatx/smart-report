using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace SmartReport.Tests;

/// <summary>
/// The response headers from rule C8, and the caching rule from A10, asserted against the real
/// middleware pipeline rather than read off the source.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SecurityHeaderTests(PostgresFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Not Development: this exercises the production branch, where the CSP is enforced
            // rather than report-only and the dev sign-in switcher must be gone.
            builder.UseEnvironment(Environments.Staging);
            builder.UseSetting("ConnectionStrings:App", fixture.AppConnectionString);
            builder.UseSetting("ConnectionStrings:Owner", fixture.OwnerConnectionString);
        });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task The_landing_page_carries_every_security_header()
    {
        var response = await _client.GetAsync("/");
        var headers = response.Headers;

        Assert.Equal("nosniff", Assert.Single(headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY", Assert.Single(headers.GetValues("X-Frame-Options")));
        Assert.Equal("strict-origin-when-cross-origin",
            Assert.Single(headers.GetValues("Referrer-Policy")));
    }

    [Fact]
    public async Task Outside_development_the_csp_is_enforced_not_merely_reported()
    {
        var response = await _client.GetAsync("/");

        Assert.True(response.Headers.Contains("Content-Security-Policy"));
        Assert.False(response.Headers.Contains("Content-Security-Policy-Report-Only"));
    }

    [Fact]
    public async Task The_csp_allows_no_third_party_script_origins_and_no_unsafe_inline()
    {
        var csp = Assert.Single((await _client.GetAsync("/")).Headers.GetValues("Content-Security-Policy"));

        Assert.Contains("script-src 'self'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.DoesNotContain("unsafe-eval", csp);
        Assert.DoesNotContain("http://", csp);
        Assert.DoesNotContain("cdn", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
    }

    [Fact]
    public async Task The_development_sign_in_switcher_does_not_exist_outside_development()
    {
        // Rule A12. This endpoint hands out an identity with no password; outside development it
        // must not be reachable at all.
        var response = await _client.PostAsync("/dev/signin-as/dana", new FormUrlEncodedContent([]));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_ask_endpoint_refuses_anyone_who_is_not_signed_in()
    {
        var response = await _client.PostAsJsonAsync("/api/reports/ask", new { question = "total revenue" });

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Per_user_pages_are_never_cached()
    {
        var response = await _client.GetAsync("/portal/reports");

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using SmartReport.Core.Reporting;
using SmartReport.App.Security;

namespace SmartReport.App.Endpoints;

public sealed record AskRequest(string Question);

public sealed record SaveRequest(string Title, string Question);

/// <summary>Server-side validation of what the browser sends (C4).</summary>
public sealed class SaveRequestValidator : AbstractValidator<SaveRequest>
{
    public SaveRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty().MaximumLength(160);
        RuleFor(r => r.Question).NotEmpty().MaximumLength(500);
    }
}

/// <summary>Server-side validation of the one field the browser sends (C4).</summary>
public sealed class AskRequestValidator : AbstractValidator<AskRequest>
{
    public AskRequestValidator()
    {
        RuleFor(r => r.Question)
            .NotEmpty().WithMessage("Ask a question first.")
            .MaximumLength(500).WithMessage("That question is too long — try 500 characters or fewer.");
    }
}

public static class ReportStreamEndpoint
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static void MapReportStream(this WebApplication app)
    {
        app.MapPost("/api/reports/ask", async (
                [FromBody] AskRequest request,
                HttpContext context,
                PrincipalContext principals,
                ReportPipeline pipeline,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                // Checked inside the handler, not only by routing (C3).
                var principal = await principals.GetAsync(cancellationToken);
                if (principal is null)
                {
                    return Results.Json(new { error = "Sign in first." }, statusCode: 401);
                }

                var validation = new AskRequestValidator().Validate(request);
                if (!validation.IsValid)
                {
                    return Results.Json(
                        new { error = validation.Errors[0].ErrorMessage }, statusCode: 400);
                }

                var logger = loggerFactory.CreateLogger("ReportStream");

                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["X-Accel-Buffering"] = "no";
                context.Response.ContentType = "text/event-stream";

                await foreach (var @event in pipeline.RunAsync(request.Question, principal, cancellationToken))
                {
                    // The pipeline lives in core and has no logger, so the record of a failure is
                    // written here, where the request context is already in hand (C5).
                    if (@event is ReportEvent.Failed failed)
                    {
                        logger.LogError("Report failed for {Principal}: {Message}",
                            principal.Key, failed.Message);
                    }

                    await WriteEventAsync(context, @event, logger, cancellationToken);
                }

                return Results.Empty;
            })
            .RequireRateLimiting("ask");

        // Saving is the only write the portal makes. Owner and client come from the resolved
        // principal, never from the body (A4), and the policy's WITH CHECK rejects the insert if
        // they disagree.
        app.MapPost("/api/reports/save", async (
            [FromBody] SaveRequest request,
            PrincipalContext principals,
            SmartReport.Data.SavedReportStore store,
            CancellationToken cancellationToken) =>
        {
            var principal = await principals.GetAsync(cancellationToken);
            if (principal is null)
            {
                return Results.Json(new { error = "Sign in first." }, statusCode: 401);
            }

            var validation = new SaveRequestValidator().Validate(request);
            if (!validation.IsValid)
            {
                return Results.Json(new { error = validation.Errors[0].ErrorMessage }, statusCode: 400);
            }

            var id = await store.SaveAsync(principal, request.Title, request.Question, cancellationToken);
            return Results.Json(new { id });
        }).RequireRateLimiting("ask");
    }

    private static async Task WriteEventAsync(
        HttpContext context, ReportEvent @event, ILogger logger, CancellationToken cancellationToken)
    {
        object payload = @event switch
        {
            ReportEvent.Thinking e => new { text = e.Text },
            ReportEvent.Planned e => new { title = e.Spec.Title, queries = e.Spec.Queries },
            ReportEvent.Guard e => new
            {
                outcome = e.Decision.Outcome,
                subject = e.Decision.Subject,
                reason = e.Decision.Reason,
                remedy = e.Decision.Remedy,
            },
            ReportEvent.Sql e => new { model = e.Model, sql = e.Text },
            ReportEvent.Rows e => new { model = e.Model, count = e.Count },
            ReportEvent.Document e => new { report = e.Report },
            ReportEvent.Findings e => new { items = e.Items, disclosure = e.Disclosure },
            ReportEvent.Refused e => new { message = e.Message, remedy = e.Remedy },
            ReportEvent.Failed e => new { message = e.Message },
            _ => new { },
        };

        try
        {
            await context.Response.WriteAsync($"event: {@event.Name}\n", cancellationToken);
            await context.Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(payload, Json)}\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The reader navigated away mid-report. Ordinary, not an error.
            logger.LogDebug("Report stream closed by the client.");
            throw;
        }
    }
}

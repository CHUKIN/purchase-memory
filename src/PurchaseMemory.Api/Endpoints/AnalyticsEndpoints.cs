using PurchaseMemory.Api.Auth;
using PurchaseMemory.Application.Analytics;

namespace PurchaseMemory.Api.Endpoints;

/// <summary>
/// Questions about the purchase history. Only <c>ask</c> calls a model (and is
/// rate limited per user); <c>run</c> executes a plan a person edited with the
/// chips and never calls a model; it has only a generous per-user limit. All three sit behind the fallback
/// «approved user» policy.
/// </summary>
public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/analytics/context", GetContextAsync).WithName("GetAnalyticsContext");
        endpoints.MapPost("/api/analytics/ask", AskAsync)
            .WithName("AskAnalyticsQuestion")
            .RequireRateLimiting(ReceiptUploadRateLimiting.AnalyticsPolicyName);
        endpoints.MapPost("/api/analytics/run", RunAsync)
            .WithName("RunAnalyticsPlan")
            .RequireRateLimiting(ReceiptUploadRateLimiting.AnalyticsRunPolicyName);
        return endpoints;
    }

    private static async Task<IResult> GetContextAsync(IAnalyticsService analytics, CancellationToken cancellationToken)
    {
        var vocabulary = await analytics.GetVocabularyAsync(cancellationToken);
        return Results.Ok(new AnalyticsContextResponse(analytics.CanAsk, analytics.Today, vocabulary.Merchants, vocabulary.Brands));
    }

    private static async Task<IResult> AskAsync(
        AnalyticsAskRequest? request,
        IAnalyticsService analytics,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Question))
        {
            return Problem(StatusCodes.Status400BadRequest, "emptyQuestion", "A question is required.");
        }

        if (request.Question.Length > AnalyticsService.MaximumQuestionLength)
        {
            return Problem(StatusCodes.Status400BadRequest, "questionTooLong",
                $"A question cannot exceed {AnalyticsService.MaximumQuestionLength} characters.");
        }

        if (!analytics.CanAsk)
        {
            return Problem(StatusCodes.Status503ServiceUnavailable, "analyticsDisabled",
                "Questions are switched off on this server. Filters can still be changed by hand.");
        }

        try
        {
            return Results.Ok(await analytics.AskAsync(request.Question, request.PreviousPlan, request.Locale, cancellationToken));
        }
        catch (AnalyticsQuestionNotUnderstoodException)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "notUnderstood",
                "The question does not look like a question about purchases.");
        }
        catch (AnalyticsPlanningException)
        {
            // Details are in the log; the model's raw output is never echoed back.
            return Problem(StatusCodes.Status502BadGateway, "planningFailed",
                "The question could not be interpreted right now. Try again or rephrase it.");
        }
    }

    private static async Task<IResult> RunAsync(
        AnalyticsRunRequest? request,
        IAnalyticsService analytics,
        CancellationToken cancellationToken)
    {
        if (request?.Plan is null)
        {
            return Problem(StatusCodes.Status400BadRequest, "emptyPlan", "A plan is required.");
        }

        return Results.Ok(await analytics.RunAsync(request.Plan, request.Locale, cancellationToken));
    }

    private static IResult Problem(int status, string code, string detail) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}

public sealed record AnalyticsAskRequest(string? Question, AnalyticsPlan? PreviousPlan, string? Locale);

public sealed record AnalyticsRunRequest(AnalyticsPlan? Plan, string? Locale);

public sealed record AnalyticsContextResponse(
    bool Enabled,
    DateOnly Today,
    IReadOnlyList<string> Merchants,
    IReadOnlyList<string> Brands);

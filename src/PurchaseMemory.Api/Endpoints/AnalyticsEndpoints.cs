using System.Security.Claims;
using PurchaseMemory.Api.Auth;
using PurchaseMemory.Application.Analytics;

namespace PurchaseMemory.Api.Endpoints;

/// <summary>
/// Questions about the purchase history. Only <c>ask</c> can call a model (and
/// is rate limited per user, and only when it really calls the model); a
/// question asked before reuses its saved plan instead and costs nothing. <c>run</c> executes a plan a person edited with the chips, and the
/// history's <c>refresh</c> recalculates a saved plan; neither calls a model and
/// both share a generous per-user limit. Everything sits behind the fallback
/// «approved user» policy. The history is the household's, like all data in
/// this version: every approved user sees and manages the same list.
/// </summary>
public static class AnalyticsEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/analytics/context", GetContextAsync).WithName("GetAnalyticsContext");
        endpoints.MapPost("/api/analytics/ask", AskAsync)
            .WithName("AskAnalyticsQuestion")
            // Cheap database bucket for every ask; the model allowance is checked in the service.
            .RequireRateLimiting(ReceiptUploadRateLimiting.AnalyticsRunPolicyName);
        endpoints.MapPost("/api/analytics/run", RunAsync)
            .WithName("RunAnalyticsPlan")
            .RequireRateLimiting(ReceiptUploadRateLimiting.AnalyticsRunPolicyName);
        endpoints.MapGet("/api/analytics/history", ListHistoryAsync).WithName("ListAnalyticsHistory");
        endpoints.MapGet("/api/analytics/history/{entryId:guid}", GetHistoryEntryAsync).WithName("GetAnalyticsHistoryEntry");
        endpoints.MapPost("/api/analytics/history/{entryId:guid}/refresh", RefreshHistoryEntryAsync)
            .WithName("RefreshAnalyticsHistoryEntry")
            .RequireRateLimiting(ReceiptUploadRateLimiting.AnalyticsRunPolicyName);
        endpoints.MapDelete("/api/analytics/history/{entryId:guid}", DeleteHistoryEntryAsync).WithName("DeleteAnalyticsHistoryEntry");
        return endpoints;
    }

    private static async Task<IResult> GetContextAsync(IAnalyticsService analytics, CancellationToken cancellationToken)
    {
        var vocabulary = await analytics.GetVocabularyAsync(cancellationToken);
        return Results.Ok(new AnalyticsContextResponse(analytics.CanAsk, analytics.Today, vocabulary.Merchants, vocabulary.Brands));
    }

    private static async Task<IResult> AskAsync(
        AnalyticsAskRequest? request,
        ClaimsPrincipal user,
        HttpContext context,
        IAnalyticsHistoryService history,
        ILoggerFactory loggerFactory,
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

        try
        {
            return Results.Ok(await history.AskAsync(
                request.Question,
                request.PreviousPlan,
                request.Locale,
                request.AskModel ?? false,
                AppUser.Actor(user),
                cancellationToken));
        }
        catch (AnalyticsRateLimitedException limited)
        {
            AuthLogMessages.UploadRateLimited(loggerFactory.CreateLogger("Auth"), AppUser.UserId(user) ?? "anonymous");
            var retryAfterSeconds = (int)Math.Ceiling((limited.RetryAfter ?? TimeSpan.FromMinutes(3)).TotalSeconds);
            context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.Problem(
                title: "Too many questions",
                detail: "Too many questions in a short time. Filters can still be changed by hand.",
                statusCode: StatusCodes.Status429TooManyRequests,
                extensions: new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfterSeconds });
        }
        catch (AnalyticsQuestionsDisabledException)
        {
            return Problem(StatusCodes.Status503ServiceUnavailable, "analyticsDisabled",
                "Questions are switched off on this server. Filters can still be changed by hand.");
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

    private static async Task<IResult> ListHistoryAsync(
        ClaimsPrincipal user,
        IAnalyticsHistoryService history,
        CancellationToken cancellationToken,
        int? offset = null,
        int? limit = null)
    {
        if (offset is < 0 || limit is < 1 or > AnalyticsHistoryService.MaximumPageSize)
        {
            return Problem(StatusCodes.Status400BadRequest, "invalidPage",
                $"offset must be 0 or more and limit between 1 and {AnalyticsHistoryService.MaximumPageSize}.");
        }

        return Results.Ok(await history.ListAsync(
            offset ?? 0,
            limit ?? AnalyticsHistoryService.DefaultPageSize,
            AppUser.Actor(user),
            cancellationToken));
    }

    private static async Task<IResult> GetHistoryEntryAsync(
        Guid entryId,
        ClaimsPrincipal user,
        IAnalyticsHistoryService history,
        CancellationToken cancellationToken) =>
        await history.GetAsync(entryId, AppUser.Actor(user), cancellationToken) is { } detail
            ? Results.Ok(detail)
            : EntryNotFound();

    /// <summary>
    /// Takes no body: the plan is the saved one, and <c>?locale=</c> picks the answer's language.
    /// An entry an older version wrote unreadably is a 422 <c>historyEntryOutdated</c>, matching its GET's <c>answer: null</c>.
    /// </summary>
    private static async Task<IResult> RefreshHistoryEntryAsync(
        Guid entryId,
        ClaimsPrincipal user,
        IAnalyticsHistoryService history,
        CancellationToken cancellationToken,
        string? locale = null)
    {
        try
        {
            return await history.RefreshAsync(entryId, locale, AppUser.Actor(user), cancellationToken) is { } detail
                ? Results.Ok(detail)
                : EntryNotFound();
        }
        catch (AnalyticsHistoryEntryOutdatedException)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "historyEntryOutdated",
                "This saved question was made by an older version and cannot be recalculated. Ask it again or delete it.");
        }
    }

    private static async Task<IResult> DeleteHistoryEntryAsync(
        Guid entryId,
        IAnalyticsHistoryService history,
        CancellationToken cancellationToken) =>
        await history.DeleteAsync(entryId, cancellationToken) ? Results.NoContent() : EntryNotFound();

    private static IResult EntryNotFound() =>
        Problem(StatusCodes.Status404NotFound, "historyEntryNotFound", "This question is not in the history.");

    private static IResult Problem(int status, string code, string detail) =>
        Results.Problem(detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}

/// <param name="AskModel">«Спросить заново»: plan with the model even if this question was asked before.</param>
public sealed record AnalyticsAskRequest(string? Question, AnalyticsPlan? PreviousPlan, string? Locale, bool? AskModel = null);

public sealed record AnalyticsRunRequest(AnalyticsPlan? Plan, string? Locale);

public sealed record AnalyticsContextResponse(
    bool Enabled,
    DateOnly Today,
    IReadOnlyList<string> Merchants,
    IReadOnlyList<string> Brands);

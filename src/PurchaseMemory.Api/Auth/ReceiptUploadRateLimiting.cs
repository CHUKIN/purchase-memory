using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace PurchaseMemory.Api.Auth;

/// <summary>
/// Per-user token bucket for requests that start paid recognition: uploading
/// a receipt and (re)queueing it. Defaults allow a burst of 10 receipts and
/// then one more every 3 minutes (about 20 an hour) for each user.
/// </summary>
public sealed class ReceiptUploadRateLimitOptions
{
    public const string SectionName = "RateLimiting:ReceiptUploads";

    public int TokenLimit { get; set; } = 10;

    public int TokensPerPeriod { get; set; } = 1;

    public int ReplenishmentPeriodSeconds { get; set; } = 180;
}

/// <summary>
/// Per-user token bucket for analytics questions, each of which calls a model:
/// a burst of 20 and then one more every 3 minutes, about 20 an hour.
/// </summary>
public sealed class AnalyticsQuestionRateLimitOptions
{
    public const string SectionName = "RateLimiting:AnalyticsQuestions";

    public int TokenLimit { get; set; } = 20;

    public int TokensPerPeriod { get; set; } = 1;

    public int ReplenishmentPeriodSeconds { get; set; } = 180;
}

public static class ReceiptUploadRateLimiting
{
    public const string PolicyName = "receipt-uploads";

    /// <summary>
    /// The category backfill also spends money on the model: per user, a burst
    /// of 3 runs and then one more every 10 minutes.
    /// </summary>
    public const string BackfillPolicyName = "categorization-backfill";

    public const string AnalyticsPolicyName = "analytics-questions";

    /// <summary>
    /// Re-running a plan costs no model call, only database work: a generous
    /// per-user bucket (120 at once, then one every 30 seconds ≈ 120 an hour).
    /// </summary>
    public const string AnalyticsRunPolicyName = "analytics-runs";

    public static IServiceCollection AddReceiptUploadRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var limits = configuration.GetSection(ReceiptUploadRateLimitOptions.SectionName).Get<ReceiptUploadRateLimitOptions>()
            ?? new ReceiptUploadRateLimitOptions();
        if (limits.TokenLimit < 1 || limits.TokensPerPeriod < 1 || limits.ReplenishmentPeriodSeconds < 1)
        {
            throw new InvalidOperationException($"{ReceiptUploadRateLimitOptions.SectionName} values must be positive.");
        }

        var questions = configuration.GetSection(AnalyticsQuestionRateLimitOptions.SectionName).Get<AnalyticsQuestionRateLimitOptions>()
            ?? new AnalyticsQuestionRateLimitOptions();
        if (questions.TokenLimit < 1 || questions.TokensPerPeriod < 1 || questions.ReplenishmentPeriodSeconds < 1)
        {
            throw new InvalidOperationException($"{AnalyticsQuestionRateLimitOptions.SectionName} values must be positive.");
        }

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(PolicyName, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    // Authorization runs first, so every request here has a subject.
                    AppUser.UserId(context.User) ?? "anonymous",
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = limits.TokenLimit,
                        TokensPerPeriod = limits.TokensPerPeriod,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(limits.ReplenishmentPeriodSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));
            options.AddPolicy(BackfillPolicyName, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    AppUser.UserId(context.User) ?? "anonymous",
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 3,
                        TokensPerPeriod = 1,
                        ReplenishmentPeriod = TimeSpan.FromMinutes(10),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(AnalyticsPolicyName, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    AppUser.UserId(context.User) ?? "anonymous",
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = questions.TokenLimit,
                        TokensPerPeriod = questions.TokensPerPeriod,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(questions.ReplenishmentPeriodSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(AnalyticsRunPolicyName, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    AppUser.UserId(context.User) ?? "anonymous",
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 120,
                        TokensPerPeriod = 1,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(30),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.OnRejected = async (rejected, cancellationToken) =>
            {
                var context = rejected.HttpContext;
                var retryAfterSeconds = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? (int)Math.Ceiling(retryAfter.TotalSeconds)
                    : limits.ReplenishmentPeriodSeconds;
                context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

                var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Auth");
                var userId = AppUser.UserId(context.User) ?? "anonymous";
                AuthLogMessages.UploadRateLimited(logger, userId);

                var backfill = context.Request.Path.StartsWithSegments("/api/products/categorization", StringComparison.Ordinal);
                var analytics = context.Request.Path.StartsWithSegments("/api/analytics", StringComparison.Ordinal);
                var rerun = context.Request.Path.StartsWithSegments("/api/analytics/run", StringComparison.Ordinal)
                    || (context.Request.Path.StartsWithSegments("/api/analytics/history", StringComparison.Ordinal)
                        && context.Request.Path.Value?.EndsWith("/refresh", StringComparison.Ordinal) == true);
                var (title, detail) = (backfill, analytics) switch
                {
                    _ when rerun => ("Too many recalculations", "Too many recalculations in a short time. Try again in a minute."),
                    (true, _) => ("Too many categorization runs", "Too many categorization runs in a short time. Try again later."),
                    (_, true) => ("Too many questions", "Too many questions in a short time. Filters can still be changed by hand."),
                    _ => ("Too many receipt uploads", "Too many receipts in a short time. Try again later.")
                };
                await Results.Problem(
                        title: title,
                        detail: detail,
                        statusCode: StatusCodes.Status429TooManyRequests,
                        extensions: new Dictionary<string, object?> { ["retryAfterSeconds"] = retryAfterSeconds })
                    .ExecuteAsync(context);
            };
        });

        return services;
    }
}

using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using PurchaseMemory.Application.Analytics;

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
/// Per-user token bucket for analytics questions that call a model: a burst of
/// 20 and then one more every 3 minutes, about 20 an hour. A question answered
/// from the history uses nothing; see <see cref="AnalyticsQuestionQuota"/>.
/// </summary>
public sealed class AnalyticsQuestionRateLimitOptions
{
    public const string SectionName = "RateLimiting:AnalyticsQuestions";

    public int TokenLimit { get; set; } = 20;

    public int TokensPerPeriod { get; set; } = 1;

    public int ReplenishmentPeriodSeconds { get; set; } = 180;
}

/// <summary>
/// Per-user token bucket for AI tag suggestions on the product page: a burst of
/// 30 and then one more every 2 minutes, about 30 an hour.
/// </summary>
public sealed class TagSuggestionRateLimitOptions
{
    public const string SectionName = "RateLimiting:TagSuggestions";

    public int TokenLimit { get; set; } = 30;

    public int TokensPerPeriod { get; set; } = 1;

    public int ReplenishmentPeriodSeconds { get; set; } = 120;
}

/// <summary>
/// Per-user token bucket for «Название по фото»: one vision call per photo, a
/// burst of 20 and then one more every 3 minutes (about 20 an hour).
/// </summary>
public sealed class NameFromPhotoRateLimitOptions
{
    public const string SectionName = "RateLimiting:NameFromPhoto";

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

    /// <summary>
    /// Re-running a plan costs no model call, only database work: a generous
    /// per-user bucket (120 at once, then one every 30 seconds ≈ 120 an hour).
    /// </summary>
    public const string AnalyticsRunPolicyName = "analytics-runs";

    public const string TagSuggestionsPolicyName = "tag-suggestions";

    /// <summary>
    /// «Предложить заново» for one product's names: one small model call, so the
    /// same per-user bucket shape as tag suggestions.
    /// </summary>
    public const string NameSuggestionsPolicyName = "name-suggestions";

    /// <summary>«Название по фото»: a paid vision call per photo, a bucket of its own.</summary>
    public const string NameFromPhotoPolicyName = "name-from-photo";

    /// <summary>
    /// Product photo uploads cost storage, not money: a burst large enough for
    /// the one-time move of a phone's photos (100), then one every 6 seconds.
    /// </summary>
    public const string ProductPhotosPolicyName = "product-photos";

    /// <summary>
    /// Open Food Facts lookups go to a free public service that asks clients to
    /// stay polite: per user, 30 at once, then one every 4 seconds.
    /// </summary>
    public const string OpenFoodFactsPolicyName = "open-food-facts";

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

        var tagSuggestions = configuration.GetSection(TagSuggestionRateLimitOptions.SectionName).Get<TagSuggestionRateLimitOptions>()
            ?? new TagSuggestionRateLimitOptions();
        if (tagSuggestions.TokenLimit < 1 || tagSuggestions.TokensPerPeriod < 1 || tagSuggestions.ReplenishmentPeriodSeconds < 1)
        {
            throw new InvalidOperationException($"{TagSuggestionRateLimitOptions.SectionName} values must be positive.");
        }

        var nameFromPhoto = configuration.GetSection(NameFromPhotoRateLimitOptions.SectionName).Get<NameFromPhotoRateLimitOptions>()
            ?? new NameFromPhotoRateLimitOptions();
        if (nameFromPhoto.TokenLimit < 1 || nameFromPhoto.TokensPerPeriod < 1 || nameFromPhoto.ReplenishmentPeriodSeconds < 1)
        {
            throw new InvalidOperationException($"{NameFromPhotoRateLimitOptions.SectionName} values must be positive.");
        }

        // Not a middleware policy: whether a question costs a model call is only
        // known after the history was looked at, so the history service asks.
        services.AddSingleton<IAnalyticsModelQuota>(_ => new AnalyticsQuestionQuota(questions));

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

            options.AddPolicy(TagSuggestionsPolicyName, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    AppUser.UserId(context.User) ?? "anonymous",
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = tagSuggestions.TokenLimit,
                        TokensPerPeriod = tagSuggestions.TokensPerPeriod,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(tagSuggestions.ReplenishmentPeriodSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(NameSuggestionsPolicyName, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    AppUser.UserId(context.User) ?? "anonymous",
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = tagSuggestions.TokenLimit,
                        TokensPerPeriod = tagSuggestions.TokensPerPeriod,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(tagSuggestions.ReplenishmentPeriodSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(NameFromPhotoPolicyName, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    AppUser.UserId(context.User) ?? "anonymous",
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = nameFromPhoto.TokenLimit,
                        TokensPerPeriod = nameFromPhoto.TokensPerPeriod,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(nameFromPhoto.ReplenishmentPeriodSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(ProductPhotosPolicyName, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    AppUser.UserId(context.User) ?? "anonymous",
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 100,
                        TokensPerPeriod = 1,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(6),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(OpenFoodFactsPolicyName, context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    AppUser.UserId(context.User) ?? "anonymous",
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 30,
                        TokensPerPeriod = 1,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(4),
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
                var tagSuggestion = context.Request.Path.Value?.EndsWith("/tag-suggestions", StringComparison.Ordinal) == true;
                var nameSuggestion = context.Request.Path.Value?.EndsWith("/name-suggestions", StringComparison.Ordinal) == true;
                var photoName = context.Request.Path.Value?.EndsWith("/name-from-photo", StringComparison.Ordinal) == true;
                var productPhoto = context.Request.Path.StartsWithSegments("/api/products", StringComparison.Ordinal)
                    && context.Request.Path.Value?.EndsWith("/photos", StringComparison.Ordinal) == true;
                var openFoodFacts = context.Request.Path.StartsWithSegments("/api/open-food-facts", StringComparison.Ordinal);
                var (title, detail) = (backfill, analytics) switch
                {
                    _ when productPhoto => ("Too many photos", "Too many product photos in a short time. Try again in a minute."),
                    _ when openFoodFacts => ("Too many lookups", "Too many product database lookups in a short time. Try again in a minute."),
                    _ when photoName => ("Too many photo names", "Too many names from photos in a short time. Names can still be typed by hand."),
                    _ when nameSuggestion => ("Too many name suggestions", "Too many name suggestions in a short time. Names can still be typed by hand."),
                    _ when tagSuggestion => ("Too many tag suggestions", "Too many tag suggestions in a short time. Tags can still be chosen by hand."),
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

/// <summary>One token bucket per user for model-backed analytics questions.</summary>
public sealed class AnalyticsQuestionQuota : IAnalyticsModelQuota, IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;
    private readonly TimeSpan _fallbackRetryAfter;

    public AnalyticsQuestionQuota(AnalyticsQuestionRateLimitOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _fallbackRetryAfter = TimeSpan.FromSeconds(options.ReplenishmentPeriodSeconds);
        _limiter = PartitionedRateLimiter.Create<string, string>(userId =>
            RateLimitPartition.GetTokenBucketLimiter(
                userId,
                _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = options.TokenLimit,
                    TokensPerPeriod = options.TokensPerPeriod,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(options.ReplenishmentPeriodSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
    }

    public TimeSpan? TryAcquire(string userId)
    {
        using var lease = _limiter.AttemptAcquire(userId);
        if (lease.IsAcquired)
        {
            return null;
        }

        return lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : _fallbackRetryAfter;
    }

    public void Dispose() => _limiter.Dispose();
}

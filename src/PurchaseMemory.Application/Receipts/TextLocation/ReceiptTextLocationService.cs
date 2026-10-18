using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Receipts.TextLocation;

/// <summary>
/// Limits of the background text-location step. The defaults fit the free
/// Azure tier (F0: 500 pages a month, about 20 calls a minute) with a margin:
/// a receipt costs one page and 2–4 HTTP calls.
/// </summary>
public sealed class ReceiptTextLocationOptions
{
    public const string SectionName = "TextLocation";

    /// <summary>Off switch even when the Azure variables are set.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Service calls per calendar month (UTC), new receipts and backfill together.</summary>
    public int MonthlyCallLimit { get; set; } = 450;

    /// <summary>Backfill of older receipts stops at this many calls a month, keeping the rest for new receipts.</summary>
    public int BackfillMonthlyCallLimit { get; set; } = 300;

    /// <summary>Receipts created within this window count as new, older ones as backfill.</summary>
    public int NewReceiptWindowHours { get; set; } = 48;

    /// <summary>Calls per receipt before it is given up (passing failures only).</summary>
    public int MaximumAttempts { get; set; } = 3;

    public int RetryDelayMinutes { get; set; } = 60;

    /// <summary>Pause between two receipts sent to the service.</summary>
    public int SecondsBetweenReceipts { get; set; } = 10;

    /// <summary>The worker starts this long after the application, so startup never waits for it.</summary>
    public int StartupDelaySeconds { get; set; } = 60;

    /// <summary>With nothing to do, the worker looks again after this long (or as soon as a receipt is ready).</summary>
    public int IdlePollMinutes { get; set; } = 15;

    /// <summary>After the service refused the key, nothing is sent for this long.</summary>
    public int NotAuthorizedPauseMinutes { get; set; } = 360;

    public bool IsValid() =>
        MonthlyCallLimit is >= 0 and <= 100_000
        && BackfillMonthlyCallLimit >= 0 && BackfillMonthlyCallLimit <= MonthlyCallLimit
        && NewReceiptWindowHours is >= 1 and <= 24 * 30
        && MaximumAttempts is >= 1 and <= 10
        && RetryDelayMinutes is >= 1 and <= 24 * 60
        && SecondsBetweenReceipts is >= 1 and <= 3600
        && StartupDelaySeconds is >= 0 and <= 3600
        && IdlePollMinutes is >= 1 and <= 24 * 60
        && NotAuthorizedPauseMinutes is >= 1 and <= 7 * 24 * 60;
}

public enum ReceiptTextLocationOutcome
{
    /// <summary>No service configured, or switched off.</summary>
    Disabled,

    /// <summary>No receipt waits (within the budget that is left).</summary>
    Idle,

    /// <summary>This month's calls are used up.</summary>
    BudgetExhausted,

    /// <summary>A receipt was marked without a call (several photos, photo missing or too large).</summary>
    Skipped,

    /// <summary>A layout was stored and the lines matched against it.</summary>
    Located,

    /// <summary>The call failed; the receipt is retried later or given up.</summary>
    Failed,

    /// <summary>Rate limited or not authorized: wait <see cref="ReceiptTextLocationRun.Wait"/> before the next call.</summary>
    Paused
}

public sealed record ReceiptTextLocationRun(ReceiptTextLocationOutcome Outcome, Guid? ReceiptId = null, TimeSpan? Wait = null)
{
    public bool CalledService => Outcome is ReceiptTextLocationOutcome.Located
        or ReceiptTextLocationOutcome.Failed or ReceiptTextLocationOutcome.Paused;
}

public interface IReceiptTextLocationService
{
    /// <summary>Handles at most one receipt: the newest one waiting, new receipts before the backfill.</summary>
    Task<ReceiptTextLocationRun> LocateNextAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Sends one waiting receipt photo to the text locator, stores the layout and
/// matches the receipt's lines to it. The same loop serves new receipts and
/// the backfill of old ones: every state lives in the database, so it is
/// idempotent, resumes after a restart and never needs a separate job.
/// </summary>
public sealed partial class ReceiptTextLocationService(
    IReceiptTextLocator locator,
    IReceiptTextLayoutRepository layouts,
    IReceiptCaptureRepository receipts,
    IReceiptImageStore images,
    ReceiptTextLocationOptions options,
    TimeProvider timeProvider,
    ILogger<ReceiptTextLocationService> logger) : IReceiptTextLocationService
{
    private static readonly TimeSpan DefaultRateLimitPause = TimeSpan.FromMinutes(1);

    public async Task<ReceiptTextLocationRun> LocateNextAsync(CancellationToken cancellationToken)
    {
        if (!options.Enabled || !locator.IsConfigured)
        {
            return new ReceiptTextLocationRun(ReceiptTextLocationOutcome.Disabled);
        }

        var now = timeProvider.GetUtcNow();
        var monthStart = new DateTimeOffset(now.UtcDateTime.Year, now.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var used = await layouts.CountCallsSinceAsync(monthStart, cancellationToken);
        if (used >= options.MonthlyCallLimit)
        {
            return new ReceiptTextLocationRun(ReceiptTextLocationOutcome.BudgetExhausted);
        }

        var newOnly = used >= options.BackfillMonthlyCallLimit;
        var candidate = await layouts.FindNextCandidateAsync(
            now.AddMinutes(-options.RetryDelayMinutes),
            newOnly ? now.AddHours(-options.NewReceiptWindowHours) : null,
            options.MaximumAttempts,
            cancellationToken);
        if (candidate is null)
        {
            return new ReceiptTextLocationRun(newOnly
                ? ReceiptTextLocationOutcome.BudgetExhausted
                : ReceiptTextLocationOutcome.Idle);
        }

        if (candidate.ImageParts.Count != 1)
        {
            // Multi-photo receipts stay out of scope: recognition boxes do not
            // say which photo they belong to, so the UI shows such receipts whole.
            return await SkipAsync(candidate.ReceiptId, "several_photos", cancellationToken);
        }

        var photo = await ReadPhotoAsync(candidate.ImageParts[0], cancellationToken);
        if (photo is null)
        {
            // Storage may be briefly unreachable: a passing failure, retried a few times, no call.
            return await RecordNotSentAsync(candidate.ReceiptId, cancellationToken);
        }

        if (photo.Value.Bytes.Length > locator.MaximumImageBytes)
        {
            return await SkipAsync(candidate.ReceiptId, "photo_too_large", cancellationToken);
        }

        // The call is counted and saved before it is made, so nothing that goes
        // wrong afterwards can lead to an uncounted (billed) retry.
        var receipt = await receipts.FindByIdAsync(candidate.ReceiptId, cancellationToken);
        if (receipt is null)
        {
            return new ReceiptTextLocationRun(ReceiptTextLocationOutcome.Skipped, candidate.ReceiptId);
        }

        receipt.BeginTextLocationCall(timeProvider.GetUtcNow());
        await receipts.SaveChangesAsync(cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        ReceiptTextLocatorResult result;
        try
        {
            result = await locator.ReadAsync(photo.Value.Bytes, photo.Value.ContentType, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            HandlingFailed(candidate.ReceiptId, "call", exception.GetType().Name);
            result = ReceiptTextLocatorResult.Failed(ReceiptTextLocatorFailure.Transient);
        }

        stopwatch.Stop();
        try
        {
            return await RecordAsync(candidate.ReceiptId, result, stopwatch.ElapsedMilliseconds, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // A failed save (e.g. a review saved at the same moment) or a bug in
            // matching: the call is already counted; record a passing failure
            // on a clean reload so the receipt waits for its retry slot.
            HandlingFailed(candidate.ReceiptId, "record", exception.GetType().Name);
            return await RecordFailureAfterErrorAsync(candidate.ReceiptId, cancellationToken);
        }
    }

    private async Task<ReceiptTextLocationRun> RecordAsync(
        Guid receiptId,
        ReceiptTextLocatorResult result,
        long elapsedMilliseconds,
        CancellationToken cancellationToken)
    {
        // Reloaded after the slow call, so a review saved meanwhile is matched
        // in its latest form.
        layouts.DiscardPendingChanges();
        var receipt = await receipts.FindByIdAsync(receiptId, cancellationToken);
        if (receipt is null)
        {
            return new ReceiptTextLocationRun(ReceiptTextLocationOutcome.Skipped, receiptId);
        }

        if (result.Page is { } page)
        {
            var layout = ReceiptTextLayout.Create(receipt.Id, page.Source, page.Width, page.Height, page.Lines, timeProvider.GetUtcNow());
            await layouts.ReplaceAsync(layout, cancellationToken);
            receipt.RecordTextLocated();
            ReceiptTextHighlighter.Apply(receipt, layout);
            await receipts.SaveChangesAsync(cancellationToken);
            var matched = receipt.Items.Count(item => item.BoxSource == ReceiptBoxSource.Azure);
            TextLocated(receipt.Id, page.Lines.Count, matched, receipt.Items.Count, elapsedMilliseconds);
            return new ReceiptTextLocationRun(ReceiptTextLocationOutcome.Located, receipt.Id);
        }

        var failure = result.Failure ?? ReceiptTextLocatorFailure.Transient;
        if (failure is ReceiptTextLocatorFailure.RateLimited or ReceiptTextLocatorFailure.NotAuthorized)
        {
            // The service, not the photo: the receipt keeps its tries (the call
            // still counts toward the monthly budget) and the worker pauses.
            receipt.ReleaseTextLocationAttempt();
        }
        else
        {
            receipt.RecordTextLocationFailed(failure == ReceiptTextLocatorFailure.Rejected, options.MaximumAttempts);
        }

        await receipts.SaveChangesAsync(cancellationToken);
        TextLocationFailed(receipt.Id, failure, receipt.TextLocationAttempts, receipt.TextLocationStatus, elapsedMilliseconds);
        return failure switch
        {
            ReceiptTextLocatorFailure.RateLimited => new ReceiptTextLocationRun(
                ReceiptTextLocationOutcome.Paused, receipt.Id, RateLimitPause(result.RetryAfter)),
            ReceiptTextLocatorFailure.NotAuthorized => new ReceiptTextLocationRun(
                ReceiptTextLocationOutcome.Paused, receipt.Id, TimeSpan.FromMinutes(options.NotAuthorizedPauseMinutes)),
            _ => new ReceiptTextLocationRun(ReceiptTextLocationOutcome.Failed, receipt.Id)
        };
    }

    /// <summary>
    /// Never shorter than the default: a Retry-After of 0 or a date in the past
    /// must not turn the pause into a tight loop of billed calls.
    /// </summary>
    internal static TimeSpan RateLimitPause(TimeSpan? retryAfter) =>
        retryAfter is { } wait && wait > DefaultRateLimitPause ? wait : DefaultRateLimitPause;

    private async Task<ReceiptTextLocationRun> RecordFailureAfterErrorAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        layouts.DiscardPendingChanges();
        var receipt = await receipts.FindByIdAsync(receiptId, cancellationToken);
        if (receipt is not null)
        {
            // The saved state already reads "Failed" with the call counted; this
            // only applies the retry limit.
            receipt.RecordTextLocationFailed(lasting: false, options.MaximumAttempts);
            await receipts.SaveChangesAsync(cancellationToken);
        }

        return new ReceiptTextLocationRun(ReceiptTextLocationOutcome.Failed, receiptId);
    }

    private async Task<ReceiptTextLocationRun> RecordNotSentAsync(Guid receiptId, CancellationToken cancellationToken)
    {
        var receipt = await receipts.FindByIdAsync(receiptId, cancellationToken);
        if (receipt is null)
        {
            return new ReceiptTextLocationRun(ReceiptTextLocationOutcome.Skipped, receiptId);
        }

        receipt.RecordTextLocationNotSent(timeProvider.GetUtcNow(), options.MaximumAttempts);
        await receipts.SaveChangesAsync(cancellationToken);
        return new ReceiptTextLocationRun(ReceiptTextLocationOutcome.Skipped, receiptId);
    }

    private async Task<ReceiptTextLocationRun> SkipAsync(Guid receiptId, string reason, CancellationToken cancellationToken)
    {
        var receipt = await receipts.FindByIdAsync(receiptId, cancellationToken);
        if (receipt is not null)
        {
            receipt.MarkTextLocationUnavailable();
            await receipts.SaveChangesAsync(cancellationToken);
            TextLocationSkipped(receiptId, reason);
        }

        return new ReceiptTextLocationRun(ReceiptTextLocationOutcome.Skipped, receiptId);
    }

    /// <summary>The display copy: what people see, so the outlines line up with it (EXIF already applied).</summary>
    private async Task<(byte[] Bytes, string ContentType)?> ReadPhotoAsync(
        ReceiptImagePart part,
        CancellationToken cancellationToken)
    {
        try
        {
            var image = await images.OpenReadAsync(part.StorageKey, ReceiptImagePurpose.Viewing, cancellationToken);
            await using var content = image.Content;
            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            return (buffer.ToArray(), image.ContentType ?? part.ContentType);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            PhotoReadFailed(part.StorageKey.Length, exception.GetType().Name);
            return null;
        }
    }

    [LoggerMessage(EventId = 3300, Level = LogLevel.Information,
        Message = "Text located for receipt {ReceiptId}: {LineCount} OCR lines, {MatchedCount}/{ItemCount} receipt lines matched in {ElapsedMs} ms")]
    private partial void TextLocated(Guid receiptId, int lineCount, int matchedCount, int itemCount, long elapsedMs);

    [LoggerMessage(EventId = 3301, Level = LogLevel.Warning,
        Message = "Text location failed for receipt {ReceiptId}: {Failure} (attempt {Attempt}, now {Status}) after {ElapsedMs} ms; the recognition box stays")]
    private partial void TextLocationFailed(Guid receiptId, ReceiptTextLocatorFailure failure, int attempt, ReceiptTextLocationStatus status, long elapsedMs);

    [LoggerMessage(EventId = 3302, Level = LogLevel.Information,
        Message = "Text location skipped for receipt {ReceiptId}: {Reason}")]
    private partial void TextLocationSkipped(Guid receiptId, string reason);

    [LoggerMessage(EventId = 3304, Level = LogLevel.Error,
        Message = "Text location for receipt {ReceiptId} failed at {Stage} ({ExceptionType}); the call is counted, the receipt is retried later")]
    private partial void HandlingFailed(Guid receiptId, string stage, string exceptionType);

    [LoggerMessage(EventId = 3303, Level = LogLevel.Warning,
        Message = "Receipt photo could not be read for text location ({ExceptionType}, key length {KeyLength})")]
    private partial void PhotoReadFailed(int keyLength, string exceptionType);
}

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Recognition;

/// <summary>
/// Selects a receipt-recognition pipeline by its stable configuration identifier
/// and tries the configured fallbacks in order.
/// </summary>
public sealed partial class ReceiptExtractorRouter : IReceiptExtractor, IReceiptRecognitionStatusProvider
{
    private readonly Dictionary<string, IReceiptRecognitionPipeline> _pipelines;
    private readonly ReceiptRecognitionOptions _options;
    private readonly ILogger<ReceiptExtractorRouter> _logger;

    public ReceiptExtractorRouter(
        IEnumerable<IReceiptRecognitionPipeline> pipelines,
        IOptions<ReceiptRecognitionOptions> options,
        ILogger<ReceiptExtractorRouter> logger)
    {
        _pipelines = CreatePipelineIndex(pipelines);
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ReceiptExtractionResult> ExtractAsync(
        IReadOnlyList<ReceiptImageData> images,
        CancellationToken cancellationToken)
    {
        var route = GetConfiguredRoute();
        var onlyPipeline = route.Length == 1 ? route[0].Pipeline : null;
        if (onlyPipeline is not null)
        {
            var onlyResult = await onlyPipeline.ExtractAsync(images, cancellationToken);
            return ReceiptExtractionPostprocessor.Apply(onlyResult);
        }

        var failures = new List<Exception>();

        for (var index = 0; index < route.Length; index++)
        {
            var candidate = route[index];
            if (candidate.Pipeline is null)
            {
                failures.Add(new InvalidOperationException(
                    $"Receipt-recognition pipeline '{candidate.Id}' is not registered."));
                continue;
            }

            try
            {
                var result = await candidate.Pipeline.ExtractAsync(images, cancellationToken);
                return ReceiptExtractionPostprocessor.Apply(result);
            }
            catch (ReceiptExtractionException exception)
            {
                failures.Add(exception);
                var nextPipeline = route.Skip(index + 1).FirstOrDefault(entry => entry.Pipeline is not null);
                if (nextPipeline is not null)
                {
                    PipelineFailedUsingFallback(candidate.Id, nextPipeline.Id, exception.Message);
                }
            }
        }

        throw new ReceiptExtractionException(
            $"None of the configured receipt-recognition pipelines succeeded: {FormatRoute(route)}.",
            new AggregateException(failures));
    }

    public async Task<ReceiptRecognitionStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        // Pipelines are checked in route order and the check stops at the first
        // available one: a fallback is probed only when it would actually be used.
        // In the cloud the primary is OpenAI and the configured Ollama fallback has
        // no daemon, so probing it on every page load would only produce
        // connection errors for a pipeline nobody is about to call.
        var route = GetConfiguredRoute();
        var unavailable = new List<PipelineStatusEntry>();

        for (var index = 0; index < route.Length; index++)
        {
            var entry = await GetStatusAsync(route[index], cancellationToken);
            if (!entry.Status.Available)
            {
                unavailable.Add(entry);
                continue;
            }

            var mode = index == 0
                ? route.Length == 1
                    ? entry.Entry.Id
                    : $"{entry.Entry.Id} · fallback: {string.Join(", ", route.Skip(1).Select(fallback => fallback.Id))}"
                : $"{entry.Entry.Id} fallback";

            return new ReceiptRecognitionStatus(
                true,
                mode,
                entry.Status.Models,
                unavailable.Count == 0 ? null : string.Join(' ', unavailable.Select(FormatUnavailableStatus)));
        }

        return new ReceiptRecognitionStatus(
            false,
            FormatRoute(route),
            [],
            string.Join(' ', unavailable.Select(FormatUnavailableStatus)));
    }

    private static Dictionary<string, IReceiptRecognitionPipeline> CreatePipelineIndex(
        IEnumerable<IReceiptRecognitionPipeline> pipelines)
    {
        var pipelineList = pipelines.ToArray();
        var invalid = pipelineList.FirstOrDefault(pipeline => string.IsNullOrWhiteSpace(pipeline.Id));
        if (invalid is not null)
        {
            throw new InvalidOperationException(
                $"Receipt-recognition pipeline '{invalid.GetType().Name}' has an empty identifier.");
        }

        var duplicate = pipelineList
            .GroupBy(pipeline => pipeline.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"More than one receipt-recognition pipeline is registered as '{duplicate.Key}'.");
        }

        return pipelineList.ToDictionary(
            pipeline => pipeline.Id,
            StringComparer.OrdinalIgnoreCase);
    }

    private PipelineRouteEntry[] GetConfiguredRoute()
    {
        var primaryId = _options.Pipeline.Trim();
        if (string.IsNullOrWhiteSpace(primaryId))
        {
            throw new InvalidOperationException(
                $"{ReceiptRecognitionOptions.SectionName}:Pipeline must not be empty.");
        }

        var pipelineIds = new[] { primaryId }
            .Concat(_options.FallbackPipelines ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);

        return pipelineIds
            .Select(id => new PipelineRouteEntry(
                id,
                _pipelines.GetValueOrDefault(id)))
            .ToArray();
    }

    private async Task<PipelineStatusEntry> GetStatusAsync(
        PipelineRouteEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.Pipeline is null)
        {
            return new PipelineStatusEntry(
                entry,
                new ReceiptRecognitionStatus(
                    false,
                    entry.Id,
                    [],
                    "The pipeline is not registered."));
        }

        try
        {
            var status = await entry.Pipeline.GetStatusAsync(cancellationToken);
            return new PipelineStatusEntry(entry, status);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            PipelineStatusCheckFailed(entry.Id, exception.GetType().Name);
            return new PipelineStatusEntry(
                entry,
                new ReceiptRecognitionStatus(
                    false,
                    entry.Id,
                    [],
                    "The pipeline status check failed."));
        }
    }

    private static string FormatRoute(IEnumerable<PipelineRouteEntry> route) =>
        string.Join(" -> ", route.Select(entry => entry.Id));

    private static string FormatUnavailableStatus(PipelineStatusEntry entry) =>
        $"{entry.Entry.Id}: {entry.Status.Message ?? "unavailable"}";

    [LoggerMessage(
        EventId = 2301,
        Level = LogLevel.Warning,
        Message = "Receipt pipeline {PipelineId} was rejected; using {FallbackId}. Reason: {Reason}")]
    private partial void PipelineFailedUsingFallback(
        string pipelineId,
        string fallbackId,
        string reason);

    [LoggerMessage(
        EventId = 2302,
        Level = LogLevel.Warning,
        Message = "Receipt pipeline {PipelineId} status check failed with {ExceptionType}.")]
    private partial void PipelineStatusCheckFailed(string pipelineId, string exceptionType);

    private sealed record PipelineRouteEntry(
        string Id,
        IReceiptRecognitionPipeline? Pipeline);

    private sealed record PipelineStatusEntry(
        PipelineRouteEntry Entry,
        ReceiptRecognitionStatus Status);
}

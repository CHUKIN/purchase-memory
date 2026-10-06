using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Recognition;

public sealed partial class OpenAiStructuredVisionProvider : IStructuredVisionProvider, IDisposable
{
    public const string ProviderId = "openai";
    public const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";

    private readonly ReceiptRecognitionOptions _options;
    private readonly ILogger<OpenAiStructuredVisionProvider> _logger;
    private readonly IOpenAiResponsesTransport? _testTransport;
    private readonly Func<string?> _readApiKey;
    private readonly Lazy<OpenAiResponsesTransport> _productionTransport;

    public OpenAiStructuredVisionProvider(
        IOptions<ReceiptRecognitionOptions> options,
        ILogger<OpenAiStructuredVisionProvider> logger)
        : this(options, logger, null, () => Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable))
    {
    }

    internal OpenAiStructuredVisionProvider(
        IOptions<ReceiptRecognitionOptions> options,
        ILogger<OpenAiStructuredVisionProvider> logger,
        IOpenAiResponsesTransport? testTransport,
        Func<string?> readApiKey)
    {
        _options = options.Value;
        _logger = logger;
        _testTransport = testTransport;
        _readApiKey = readApiKey;
        _productionTransport = new Lazy<OpenAiResponsesTransport>(
            () => new OpenAiResponsesTransport(
                _readApiKey()
                    ?? throw new InvalidOperationException("OpenAI API key is not configured."),
                _options.OpenAiModel,
                TimeSpan.FromSeconds(_options.OpenAiRequestTimeoutSeconds)),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Id => ProviderId;

    public async Task<StructuredVisionResponse> GenerateAsync(
        StructuredVisionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Images.Count == 0 || request.Images.Any(image => image.Content.IsEmpty))
        {
            throw new ReceiptExtractionException("The receipt images are empty.");
        }

        var apiKey = _readApiKey();
        if (_testTransport is null && string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ReceiptExtractionException(
                $"OpenAI is not configured. Set {ApiKeyEnvironmentVariable} on the backend.");
        }

        if (string.IsNullOrWhiteSpace(_options.OpenAiModel))
        {
            throw new ReceiptExtractionException("The OpenAI model is not configured.");
        }

        var transport = _testTransport ?? _productionTransport.Value;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await transport.CreateAsync(
                new OpenAiResponsesRequest(
                    request.Images,
                    request.Prompt,
                    request.JsonSchema,
                    _options.OpenAiModel,
                    _options.OpenAiImageDetail,
                    _options.OpenAiReasoningEffort,
                    _options.OpenAiMaximumOutputTokens),
                cancellationToken);
            stopwatch.Stop();

            if (!string.IsNullOrWhiteSpace(response.Refusal))
            {
                throw new ReceiptExtractionException(
                    "OpenAI refused to analyze this receipt. Try another image or provider.");
            }

            if (string.Equals(response.Status, "incomplete", StringComparison.OrdinalIgnoreCase))
            {
                throw new ReceiptExtractionException(
                    $"OpenAI returned an incomplete result ({response.IncompleteReason ?? "unknown reason"}).");
            }

            if (!string.Equals(response.Status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                throw new ReceiptExtractionException(
                    $"OpenAI did not complete the request (status: {response.Status ?? "unknown"}).");
            }

            if (string.IsNullOrWhiteSpace(response.Json))
            {
                throw new ReceiptExtractionException("OpenAI returned an empty structured result.");
            }

            RequestCompleted(
                _options.OpenAiModel,
                stopwatch.ElapsedMilliseconds,
                response.RequestId,
                response.InputTokens,
                response.OutputTokens);
            return new StructuredVisionResponse(
                response.Json,
                Id,
                _options.OpenAiModel,
                response.RequestId,
                response.Status,
                response.InputTokens,
                response.OutputTokens,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException exception)
        {
            RequestFailed("timeout");
            throw new ReceiptExtractionException(
                "OpenAI recognition timed out. Retry or choose another provider.",
                exception);
        }
        catch (OpenAiServiceException exception)
            when (exception.StatusCode == 0 && ContainsInner<TaskCanceledException>(exception))
        {
            RequestFailed("timeout");
            throw new ReceiptExtractionException(
                "OpenAI recognition timed out. Retry or choose another provider.",
                exception);
        }
        catch (OpenAiServiceException exception)
            when (exception.StatusCode == 0 && ContainsInner<HttpRequestException>(exception))
        {
            RequestFailed("network");
            throw new ReceiptExtractionException(
                "OpenAI could not be reached. Check the network and retry.",
                exception);
        }
        catch (OpenAiServiceException exception) when (exception.StatusCode == 401)
        {
            RequestFailed("unauthorized");
            throw new ReceiptExtractionException(
                "OpenAI rejected the backend credential. Check OPENAI_API_KEY.",
                exception);
        }
        catch (OpenAiServiceException exception) when (exception.StatusCode == 429)
        {
            RequestFailed("rate_limited");
            throw new ReceiptExtractionException(
                "OpenAI rate limit was reached. Retry later or choose another provider.",
                exception);
        }
        catch (OpenAiServiceException exception)
        {
            RequestFailed($"http_{exception.StatusCode}");
            throw new ReceiptExtractionException(
                "OpenAI rejected the recognition request. Retry or choose another provider.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            RequestFailed("network");
            throw new ReceiptExtractionException(
                "OpenAI could not be reached. Check the network and retry.",
                exception);
        }
    }

    private static bool ContainsInner<TException>(Exception exception)
        where TException : Exception
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException)
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_productionTransport.IsValueCreated)
        {
            _productionTransport.Value.Dispose();
        }
    }

    public Task<StructuredVisionProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(_options.OpenAiModel))
        {
            return Task.FromResult(new StructuredVisionProviderStatus(
                false,
                Id,
                [],
                "OpenAI model is not configured."));
        }

        if (string.IsNullOrWhiteSpace(_readApiKey()))
        {
            return Task.FromResult(new StructuredVisionProviderStatus(
                false,
                Id,
                [_options.OpenAiModel],
                $"Set {ApiKeyEnvironmentVariable} on the backend."));
        }

        return Task.FromResult(new StructuredVisionProviderStatus(
            true,
            Id,
            [_options.OpenAiModel],
            "Configured. No paid model call was made."));
    }

    [LoggerMessage(
        EventId = 2201,
        Level = LogLevel.Information,
        Message = "OpenAI provider completed with model {Model} in {DurationMs} ms; request {RequestId}; input tokens {InputTokens}; output tokens {OutputTokens}")]
    private partial void RequestCompleted(
        string model,
        long durationMs,
        string? requestId,
        int? inputTokens,
        int? outputTokens);

    [LoggerMessage(
        EventId = 2202,
        Level = LogLevel.Warning,
        Message = "OpenAI provider request failed with category {Category}")]
    private partial void RequestFailed(string category);
}

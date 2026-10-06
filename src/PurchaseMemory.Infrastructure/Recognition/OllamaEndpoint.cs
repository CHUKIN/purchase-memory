using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace PurchaseMemory.Infrastructure.Recognition;

/// <summary>
/// Where the Ollama providers send their requests: the local daemon or the
/// hosted API at https://ollama.com. Both speak the same <c>/api/chat</c> and
/// <c>/api/tags</c> protocol; the hosted API additionally needs a bearer key.
///
/// The key is never part of this type's state that is printed: <see cref="ToString"/>
/// only reports whether one is present, so an endpoint can be logged safely.
/// </summary>
public sealed class OllamaEndpoint
{
    public const string ApiKeyEnvironmentVariable = "OLLAMA_API_KEY";

    private readonly string? _apiKey;

    private OllamaEndpoint(Uri baseAddress, bool isRemote, string? apiKey)
    {
        BaseAddress = baseAddress;
        IsRemote = isRemote;
        _apiKey = apiKey;
    }

    /// <summary>Base address with a trailing slash, so relative paths like <c>api/chat</c> resolve under it.</summary>
    public Uri BaseAddress { get; }

    /// <summary><c>true</c> for anything other than a loopback daemon.</summary>
    public bool IsRemote { get; }

    public bool HasApiKey => _apiKey is not null;

    /// <summary>
    /// Validates the address/key combination before any request is made:
    /// a remote endpoint must use HTTPS (the key would otherwise travel in clear text)
    /// and must have a key, because the hosted API rejects anonymous calls anyway and
    /// a clear local error is better than a 401 recorded as a model failure.
    /// </summary>
    public static OllamaEndpoint Create(string baseAddress, string? apiKey)
    {
        if (!Uri.TryCreate(baseAddress?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("The Ollama base address must be an absolute HTTP or HTTPS URI.", nameof(baseAddress));
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            // Credentials in the URL would end up in every log line that prints the
            // address; the key belongs in OLLAMA_API_KEY only. The value is not echoed.
            throw new ArgumentException(
                "The Ollama base address must not contain a user name or password. "
                + $"Pass the key through {ApiKeyEnvironmentVariable} instead.",
                nameof(baseAddress));
        }

        var normalized = new Uri(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/", UriKind.Absolute);
        var key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        var isRemote = !normalized.IsLoopback;
        if (isRemote && normalized.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                $"The remote Ollama endpoint {normalized} must use HTTPS.");
        }

        if (isRemote && key is null)
        {
            throw new InvalidOperationException(
                $"The remote Ollama endpoint {normalized} needs an API key. "
                + $"Set the {ApiKeyEnvironmentVariable} environment variable and retry.");
        }

        return new OllamaEndpoint(normalized, isRemote, key);
    }

    /// <summary>Creates a client for this endpoint; the key, when present, is added by a handler.</summary>
    public HttpClient CreateClient(TimeSpan timeout, HttpMessageHandler innerHandler, bool disposeHandler = true) =>
        new(new OllamaApiKeyHandler(_apiKey) { InnerHandler = innerHandler }, disposeHandler)
        {
            BaseAddress = BaseAddress,
            Timeout = timeout
        };

    public override string ToString() =>
        $"{BaseAddress} ({(IsRemote ? "remote" : "local")}, {(HasApiKey ? "API key set" : "no API key")})";
}

/// <summary>
/// Adds <c>Authorization: Bearer</c> only when a key was supplied. Kept as a
/// handler so the providers themselves stay unaware of authentication.
/// </summary>
public sealed class OllamaApiKeyHandler : DelegatingHandler
{
    private readonly string? _apiKey;

    public OllamaApiKeyHandler(string? apiKey)
    {
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_apiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public enum OllamaFailureKind
{
    None,
    /// <summary>HTTP 429: the account's usage limit, not a model problem.</summary>
    RateLimited,
    /// <summary>HTTP 401/403: missing, wrong or insufficient API key.</summary>
    Unauthorized,
    /// <summary>Any other non-success HTTP status (unknown model, server error).</summary>
    HttpError,
    /// <summary>No HTTP response at all: DNS, TLS, connection refused.</summary>
    Unreachable,
    Timeout,
    /// <summary>The transport succeeded but the model's output was unusable.</summary>
    ModelError
}

public sealed record OllamaRequestRecord(
    string Method,
    string Path,
    int? StatusCode,
    long RequestBytes,
    long ElapsedMilliseconds);

/// <summary>
/// Measures every request that passes through it — status, body size and time to
/// response — so a benchmark can tell transport failures (limit, key) apart from
/// model failures without the providers having to expose HTTP details.
/// Records only the path, never headers or bodies.
/// </summary>
public sealed class OllamaRequestRecorder : DelegatingHandler
{
    private readonly Lock _lock = new();
    private readonly List<OllamaRequestRecord> _records = [];

    /// <summary>Returns and clears everything recorded since the previous call.</summary>
    public IReadOnlyList<OllamaRequestRecord> TakeRecords()
    {
        lock (_lock)
        {
            var snapshot = _records.ToArray();
            _records.Clear();
            return snapshot;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        long bytes = 0;
        if (request.Content is not null)
        {
            // JsonContent has no length until serialized; buffering once gives the
            // exact byte count and the same buffer is then sent on the wire.
            await request.Content.LoadIntoBufferAsync(cancellationToken);
            bytes = request.Content.Headers.ContentLength ?? 0;
        }

        var path = request.RequestUri?.AbsolutePath ?? "";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            Add(new(request.Method.Method, path, (int)response.StatusCode, bytes, stopwatch.ElapsedMilliseconds));
            return response;
        }
        catch
        {
            Add(new(request.Method.Method, path, null, bytes, stopwatch.ElapsedMilliseconds));
            throw;
        }
    }

    private void Add(OllamaRequestRecord record)
    {
        lock (_lock)
        {
            _records.Add(record);
        }
    }
}

public static class OllamaFailureClassifier
{
    /// <summary>
    /// Classifies one attempt from the requests it made and the exception it ended with.
    /// HTTP evidence wins over the exception text, because the providers wrap every
    /// transport error into the same user-facing exception type.
    /// </summary>
    public static OllamaFailureKind Classify(IReadOnlyList<OllamaRequestRecord> records, Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(records);
        var rejected = records.LastOrDefault(record => record.StatusCode is not null and not (>= 200 and < 300));
        if (rejected is not null)
        {
            return rejected.StatusCode switch
            {
                (int)HttpStatusCode.TooManyRequests => OllamaFailureKind.RateLimited,
                (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden => OllamaFailureKind.Unauthorized,
                _ => OllamaFailureKind.HttpError
            };
        }

        if (exception is null)
        {
            return OllamaFailureKind.None;
        }

        for (var current = exception; current is not null; current = current.InnerException)
        {
            // The providers turn an HttpClient timeout into a message-only exception.
            if (current is TimeoutException or TaskCanceledException
                || current.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            {
                return OllamaFailureKind.Timeout;
            }
        }

        return records.Count > 0 && records[^1].StatusCode is null
            ? OllamaFailureKind.Unreachable
            : OllamaFailureKind.ModelError;
    }
}

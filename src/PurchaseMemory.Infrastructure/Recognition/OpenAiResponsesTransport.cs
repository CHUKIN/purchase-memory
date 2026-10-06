using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace PurchaseMemory.Infrastructure.Recognition;

internal sealed record OpenAiResponsesRequest(
    IReadOnlyList<PurchaseMemory.Application.Receipts.ReceiptImageData> Images,
    string Prompt,
    string JsonSchema,
    string Model,
    OpenAiImageDetail ImageDetail,
    OpenAiReasoningEffort ReasoningEffort,
    int MaximumOutputTokens);

internal sealed record OpenAiResponsesResult(
    string? Json,
    string? Refusal,
    string? RequestId,
    string? Status,
    string? IncompleteReason,
    int? InputTokens,
    int? OutputTokens);

internal interface IOpenAiResponsesTransport
{
    Task<OpenAiResponsesResult> CreateAsync(
        OpenAiResponsesRequest request,
        CancellationToken cancellationToken);
}

internal sealed class OpenAiServiceException : Exception
{
    public OpenAiServiceException(int statusCode, string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

/// <summary>
/// The only type that knows about the experimental Responses surface in OpenAI 2.8.0.
/// Keeping it internal prevents SDK changes from leaking into Application contracts.
/// </summary>
internal sealed class OpenAiResponsesTransport : IOpenAiResponsesTransport, IDisposable
{
    private const string StructuredOutputName = "purchase_memory_receipt_extract_v1";
    private readonly ResponsesClient _client;
    private readonly HttpClient _httpClient;

    public OpenAiResponsesTransport(
        string apiKey,
        string model,
        TimeSpan timeout,
        HttpMessageHandler? messageHandler = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _httpClient = messageHandler is null
            ? new HttpClient()
            : new HttpClient(messageHandler, disposeHandler: false);
        _httpClient.Timeout = timeout;

        var clientOptions = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(_httpClient)
        };
        _client = new ResponsesClient(model, new ApiKeyCredential(apiKey), clientOptions);
    }

    public async Task<OpenAiResponsesResult> CreateAsync(
        OpenAiResponsesRequest request,
        CancellationToken cancellationToken)
    {
        var detail = request.ImageDetail switch
        {
            OpenAiImageDetail.Low => ResponseImageDetailLevel.Low,
            OpenAiImageDetail.High => ResponseImageDetailLevel.High,
            OpenAiImageDetail.Original => new ResponseImageDetailLevel("original"),
            _ => throw new ArgumentOutOfRangeException(nameof(request), "Unknown OpenAI image detail.")
        };
        var content = new List<ResponseContentPart>
        {
            ResponseContentPart.CreateInputTextPart(request.Prompt)
        };
        content.AddRange(request.Images.Select(image =>
            ResponseContentPart.CreateInputImagePart(
                BinaryData.FromBytes(image.Content),
                image.ContentType,
                detail)));
        var options = new CreateResponseOptions
        {
            Model = request.Model,
            MaxOutputTokenCount = request.MaximumOutputTokens,
            StoredOutputEnabled = false,
            ReasoningOptions = new ResponseReasoningOptions
            {
                ReasoningEffortLevel = request.ReasoningEffort switch
                {
                    OpenAiReasoningEffort.Minimal => ResponseReasoningEffortLevel.Minimal,
                    OpenAiReasoningEffort.Low => ResponseReasoningEffortLevel.Low,
                    OpenAiReasoningEffort.Medium => ResponseReasoningEffortLevel.Medium,
                    OpenAiReasoningEffort.High => ResponseReasoningEffortLevel.High,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(request),
                        "Unknown OpenAI reasoning effort.")
                }
            },
            TextOptions = new ResponseTextOptions
            {
                TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                    StructuredOutputName,
                    BinaryData.FromString(request.JsonSchema),
                    "A structured draft extracted from a Polish retail receipt.",
                    jsonSchemaIsStrict: true)
            }
        };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(content));

        try
        {
            ClientResult<ResponseResult> result = await _client.CreateResponseAsync(
                options,
                cancellationToken);
            var response = result.Value;
            var refusal = response.OutputItems
                .OfType<MessageResponseItem>()
                .SelectMany(item => item.Content)
                .Select(part => part.Refusal)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            var requestId = result.GetRawResponse().Headers.TryGetValue("x-request-id", out var value)
                ? value
                : response.Id;

            return new OpenAiResponsesResult(
                response.GetOutputText(),
                refusal,
                requestId,
                response.Status?.ToString(),
                response.IncompleteStatusDetails?.Reason?.ToString(),
                response.Usage?.InputTokenCount,
                response.Usage?.OutputTokenCount);
        }
        catch (ClientResultException exception)
        {
            throw new OpenAiServiceException(
                exception.Status,
                "The OpenAI service rejected the request.",
                exception);
        }
    }
    public void Dispose() => _httpClient.Dispose();
}

#pragma warning restore OPENAI001

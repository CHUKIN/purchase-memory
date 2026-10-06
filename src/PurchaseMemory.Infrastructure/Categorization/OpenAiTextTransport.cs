using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI;
using OpenAI.Responses;
using PurchaseMemory.Infrastructure.Recognition;

#pragma warning disable OPENAI001

namespace PurchaseMemory.Infrastructure.Categorization;

internal sealed record OpenAiTextRequest(
    string Prompt,
    string JsonSchema,
    string Model,
    OpenAiReasoningEffort? ReasoningEffort,
    int MaximumOutputTokens,
    string SchemaName = OpenAiTextTransport.CategorizationSchemaName,
    string SchemaDescription = OpenAiTextTransport.CategorizationSchemaDescription);

internal interface IOpenAiTextTransport
{
    Task<OpenAiResponsesResult> CreateAsync(OpenAiTextRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Text-only sibling of the receipt transport. Kept separate so the receipt
/// recognition request (prompt, schema, structured output name) stays untouched.
/// </summary>
internal sealed class OpenAiTextTransport : IOpenAiTextTransport, IDisposable
{
    public const string CategorizationSchemaName = "purchase_memory_product_categories_v1";
    public const string CategorizationSchemaDescription = "Category and brand suggestions for household catalog products.";
    private readonly ResponsesClient _client;
    private readonly HttpClient _httpClient;

    public OpenAiTextTransport(string apiKey, string model, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        _httpClient = new HttpClient { Timeout = timeout };
        var clientOptions = new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(_httpClient) };
        _client = new ResponsesClient(model, new ApiKeyCredential(apiKey), clientOptions);
    }

    public async Task<OpenAiResponsesResult> CreateAsync(OpenAiTextRequest request, CancellationToken cancellationToken)
    {
        var options = new CreateResponseOptions
        {
            Model = request.Model,
            MaxOutputTokenCount = request.MaximumOutputTokens,
            StoredOutputEnabled = false,
            TextOptions = new ResponseTextOptions
            {
                TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                    request.SchemaName,
                    BinaryData.FromString(request.JsonSchema),
                    request.SchemaDescription,
                    jsonSchemaIsStrict: true)
            }
        };

        if (request.ReasoningEffort is { } effort)
        {
            options.ReasoningOptions = new ResponseReasoningOptions
            {
                ReasoningEffortLevel = effort switch
                {
                    OpenAiReasoningEffort.Minimal => ResponseReasoningEffortLevel.Minimal,
                    OpenAiReasoningEffort.Low => ResponseReasoningEffortLevel.Low,
                    OpenAiReasoningEffort.Medium => ResponseReasoningEffortLevel.Medium,
                    OpenAiReasoningEffort.High => ResponseReasoningEffortLevel.High,
                    _ => throw new ArgumentOutOfRangeException(nameof(request), "Unknown OpenAI reasoning effort.")
                }
            };
        }

        options.InputItems.Add(ResponseItem.CreateUserMessageItem(
            [ResponseContentPart.CreateInputTextPart(request.Prompt)]));

        try
        {
            ClientResult<ResponseResult> result = await _client.CreateResponseAsync(options, cancellationToken);
            var response = result.Value;
            var refusal = response.OutputItems
                .OfType<MessageResponseItem>()
                .SelectMany(item => item.Content)
                .Select(part => part.Refusal)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            return new OpenAiResponsesResult(
                response.GetOutputText(),
                refusal,
                response.Id,
                response.Status?.ToString(),
                response.IncompleteStatusDetails?.Reason?.ToString(),
                response.Usage?.InputTokenCount,
                response.Usage?.OutputTokenCount);
        }
        catch (ClientResultException exception)
        {
            throw new OpenAiServiceException(exception.Status, "The OpenAI service rejected the request.", exception);
        }
    }

    public void Dispose() => _httpClient.Dispose();
}

#pragma warning restore OPENAI001

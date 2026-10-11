using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Tags;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Persistence;
using PurchaseMemory.Infrastructure.Recognition;
using PurchaseMemory.Infrastructure.Tagging;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// AI tag suggestions: the endpoint through the real host and auth with a fake
/// suggester, and the Claude suggester over a fake transport. No network.
/// </summary>
public sealed class TagSuggestionTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SuggestionsNeedAnApprovedValidToken()
    {
        var suggester = new FakeSuggester();
        await using var host = new AuthTestHost(configureServices: services => services.AddSingleton<ITagSuggester>(suggester));
        var path = $"/api/products/{Guid.NewGuid()}/tag-suggestions";
        foreach (var token in new[]
                 {
                     null,
                     AuthTestHost.Token(key: AuthTestHost.ForeignKey),
                     AuthTestHost.Token(expires: DateTime.UtcNow.AddMinutes(-5))
                 })
        {
            using var client = host.CreateClientFor(token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(path, null, Cancel)).StatusCode);
        }

        using var pending = host.CreateClientFor(AuthTestHost.Token(access: "pending"));
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.PostAsync(path, null, Cancel)).StatusCode);
        Assert.Equal(0, suggester.Calls);
    }

    [Fact]
    public async Task WithoutTagsTheModelIsNotAsked()
    {
        var suggester = new FakeSuggester();
        await using var host = new AuthTestHost(configureServices: services => services.AddSingleton<ITagSuggester>(suggester));
        var juice = await SeedProductAsync(host);
        using var client = host.CreateClientFor(AuthTestHost.Token());

        // The call has no body; the locale is optional.
        var response = await client.PostAsync($"/api/products/{juice}/tag-suggestions", null, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(0, body.GetProperty("suggestions").GetArrayLength());
        Assert.Equal(0, body.GetProperty("offeredTagCount").GetInt32());
        Assert.Equal(0, suggester.Calls);
    }

    [Fact]
    public async Task OnlyOfferedTagsComeBackOnceEachAndTheProductsOwnTagsAreNotOffered()
    {
        var suggester = new FakeSuggester();
        await using var host = new AuthTestHost(configureServices: services => services.AddSingleton<ITagSuggester>(suggester));
        var juice = await SeedProductAsync(host);
        var (kid, promo, vegan) = await SeedTagsAsync(host, juice);
        using var client = host.CreateClientFor(AuthTestHost.Token());

        suggester.Answer =
        [
            new SuggestedTag(promo, "already on the product"),
            new SuggestedTag(Guid.NewGuid(), "a tag that does not exist"),
            new SuggestedTag(kid, "  juice   for\nkids  "),
            new SuggestedTag(kid, "repeated"),
            new SuggestedTag(vegan, new string('x', 400))
        ];
        var response = await client.PostAsync($"/api/products/{juice}/tag-suggestions?locale=en-GB", null, Cancel);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(juice, body.GetProperty("productId").GetGuid());
        Assert.Equal(2, body.GetProperty("offeredTagCount").GetInt32());
        var suggestions = body.GetProperty("suggestions").EnumerateArray().ToList();
        Assert.Equal([kid, vegan], suggestions.Select(item => item.GetProperty("tagId").GetGuid()));
        Assert.Equal("Для ребёнка", suggestions[0].GetProperty("name").GetString());
        Assert.Equal("juice for kids", suggestions[0].GetProperty("reason").GetString());
        Assert.Equal(ProductTagSuggestionService.MaximumReasonLength, suggestions[1].GetProperty("reason").GetString()!.Length);

        // The model saw only tags the product does not have yet, and the product context.
        Assert.Equal(1, suggester.Calls);
        Assert.Equal(["Веган", "Для ребёнка"], suggester.LastTags!.Select(tag => tag.Name));
        Assert.Equal(TagSuggestionLanguage.English, suggester.LastLanguage);
        Assert.Equal("SOK JABLKOWY", suggester.LastProduct!.ReceiptTexts.Single());
        Assert.Equal("lidl", suggester.LastProduct.Merchants.Single(), ignoreCase: true);

        // Nothing was applied: the product still has only its own tag.
        var product = await client.GetFromJsonAsync<JsonElement>($"/api/products/{juice}", Cancel);
        Assert.Equal([promo], product.GetProperty("tags").EnumerateArray().Select(tag => tag.GetProperty("id").GetGuid()));
    }

    [Fact]
    public async Task WhenEveryTagIsAlreadyOnTheProductTheModelIsNotAsked()
    {
        var suggester = new FakeSuggester();
        await using var host = new AuthTestHost(configureServices: services => services.AddSingleton<ITagSuggester>(suggester));
        var juice = await SeedProductAsync(host);
        var (kid, promo, vegan) = await SeedTagsAsync(host, juice);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IProductTagService>().SetProductTagsAsync(
                juice, [kid, promo, vegan], new ReceiptActor("user-anna", "anna@example.com"), Cancel);
        }

        using var client = host.CreateClientFor(AuthTestHost.Token());
        var body = await (await client.PostAsync($"/api/products/{juice}/tag-suggestions", null, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(Cancel);

        Assert.Equal(0, body.GetProperty("offeredTagCount").GetInt32());
        Assert.Equal(0, suggester.Calls);
    }

    [Fact]
    public async Task UnavailableSuggesterIsAClearProblemAndUnknownProductIsNotFound()
    {
        // The test host switches tag suggestions off: the real Claude suggester is not configured.
        await using var host = new AuthTestHost();
        var juice = await SeedProductAsync(host);
        await SeedTagsAsync(host, juice);
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var unavailable = await client.PostAsync($"/api/products/{juice}/tag-suggestions?locale=ru", null, Cancel);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.Equal("tagSuggestionsUnavailable",
            (await unavailable.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());

        var missing = await client.PostAsync($"/api/products/{Guid.NewGuid()}/tag-suggestions", null, Cancel);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task ProviderFailureIsUnavailableWithoutLeakingDetails()
    {
        var suggester = new FakeSuggester { Failure = new TagSuggestionsUnavailableException("Claude did not answer (http_500 secret-detail).") };
        await using var host = new AuthTestHost(configureServices: services => services.AddSingleton<ITagSuggester>(suggester));
        var juice = await SeedProductAsync(host);
        await SeedTagsAsync(host, juice);
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var response = await client.PostAsync($"/api/products/{juice}/tag-suggestions", null, Cancel);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync(Cancel);
        Assert.Contains("tagSuggestionsUnavailable", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-detail", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuggestionsAreRateLimitedPerUser()
    {
        var suggester = new FakeSuggester();
        await using var host = new AuthTestHost(
            settings: new Dictionary<string, string?> { ["RateLimiting:TagSuggestions:TokenLimit"] = "2" },
            configureServices: services => services.AddSingleton<ITagSuggester>(suggester));
        var juice = await SeedProductAsync(host);
        await SeedTagsAsync(host, juice);
        var path = $"/api/products/{juice}/tag-suggestions";
        using var anna = host.CreateClientFor(AuthTestHost.Token());

        Assert.Equal(HttpStatusCode.OK, (await anna.PostAsync(path, null, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anna.PostAsync(path, null, Cancel)).StatusCode);
        var limited = await anna.PostAsync(path, null, Cancel);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.Equal("Too many tag suggestions",
            (await limited.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("title").GetString());
        Assert.Equal(2, suggester.Calls);

        // Another user has a bucket of their own; saving tags by hand is never limited by it.
        using var boris = host.CreateClientFor(AuthTestHost.Token(subject: "22222222-2222-2222-2222-222222222222"));
        Assert.Equal(HttpStatusCode.OK, (await boris.PostAsync(path, null, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await anna.PutAsJsonAsync($"/api/products/{juice}/tags", new { tagIds = Array.Empty<Guid>() }, Cancel)).StatusCode);
    }

    [Fact]
    public async Task ClaudeSuggesterOffersCodesAsAnEnumAndMapsThemBack()
    {
        var kid = Guid.NewGuid();
        var vegan = Guid.NewGuid();
        var transport = new RecordingTransport(new AnthropicMessagesResult(
            """{"suggestions":[{"tag":"t2","reason":"растительный продукт"},{"tag":"t9","reason":"not offered"}]}""",
            "end_turn", null, "msg_1", 420, 60));
        using var suggester = CreateSuggester(transport);

        var result = await suggester.SuggestAsync(
            new TagSuggestionProduct("Napój owsiany", "Alpro", "Напитки / Растительное молоко", ["NAPOJ OWSIANY 1L"], ["lidl"]),
            [new TagSuggestionOption(kid, "для ребёнка"), new TagSuggestionOption(vegan, "веган")],
            TagSuggestionLanguage.Russian,
            Cancel);

        Assert.Equal([new SuggestedTag(vegan, "растительный продукт")], result);
        var request = Assert.Single(transport.Requests);
        Assert.Empty(request.Images);
        Assert.Equal("claude-haiku-5-5", request.Model);
        Assert.Equal(AnthropicEffort.Low, request.Effort);
        Assert.Contains("t1: \"для ребёнка\"", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("t2: \"веган\"", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("\"name\":\"Napój owsiany\"", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("in Russian", request.Prompt, StringComparison.Ordinal);
        Assert.DoesNotContain(kid.ToString(), request.Prompt, StringComparison.Ordinal);
        using var schema = JsonDocument.Parse(request.JsonSchema);
        var codes = schema.RootElement.GetProperty("properties").GetProperty("suggestions").GetProperty("items")
            .GetProperty("properties").GetProperty("tag").GetProperty("enum").EnumerateArray().Select(code => code.GetString());
        Assert.Equal(["t1", "t2"], codes);
    }

    [Fact]
    public async Task ClaudeSuggesterFailuresBecomeUnavailable()
    {
        var product = new TagSuggestionProduct("Chleb", null, null, [], []);
        TagSuggestionOption[] tags = [new(Guid.NewGuid(), "веган")];

        using var withoutKey = new AnthropicTagSuggester(
            Options.Create(new TagSuggestionOptions()), NullLogger<AnthropicTagSuggester>.Instance, null, () => null);
        Assert.False(withoutKey.IsConfigured);
        await Assert.ThrowsAsync<TagSuggestionsUnavailableException>(
            () => withoutKey.SuggestAsync(product, tags, TagSuggestionLanguage.Russian, Cancel));

        using var switchedOff = new AnthropicTagSuggester(
            Options.Create(new TagSuggestionOptions { Enabled = false }), NullLogger<AnthropicTagSuggester>.Instance, null, () => "key");
        Assert.False(switchedOff.IsConfigured);

        foreach (var result in new[]
                 {
                     new AnthropicMessagesResult(null, "refusal", "cyber", "m", 1, 1),
                     new AnthropicMessagesResult("{\"suggestions\":[", "max_tokens", null, "m", 1, 1),
                     new AnthropicMessagesResult("not json", "end_turn", null, "m", 1, 1),
                     new AnthropicMessagesResult("{}", "end_turn", null, "m", 1, 1)
                 })
        {
            using var suggester = CreateSuggester(new RecordingTransport(result));
            await Assert.ThrowsAsync<TagSuggestionsUnavailableException>(
                () => suggester.SuggestAsync(product, tags, TagSuggestionLanguage.Russian, Cancel));
        }

        using var failing = CreateSuggester(new ThrowingTransport(
            new AnthropicServiceException(529, "overloaded", new InvalidOperationException())));
        await Assert.ThrowsAsync<TagSuggestionsUnavailableException>(
            () => failing.SuggestAsync(product, tags, TagSuggestionLanguage.English, Cancel));
    }

    [Fact]
    public async Task TextOnlyRequestReachesTheMessagesApiWithTheEnumSchema()
    {
        string? body = null;
        using var handler = new StubHandler(async request =>
        {
            body = await request.Content!.ReadAsStringAsync(Cancel);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"msg_1","type":"message","role":"assistant","model":"claude-haiku-5-5","content":[{"type":"text","text":"{\"suggestions\":[]}"}],"stop_reason":"end_turn","stop_sequence":null,"usage":{"input_tokens":300,"output_tokens":20,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });
        using var transport = new AnthropicMessagesTransport("test-key", TimeSpan.FromSeconds(5), 0, handler);

        var result = await transport.CreateAsync(
            new AnthropicMessagesRequest([], "prompt", AnthropicTagSuggester.BuildSchema(["t1", "t2"]), "claude-haiku-5-5", AnthropicEffort.Low, 4_000),
            Cancel);

        Assert.Equal("""{"suggestions":[]}""", result.Json);
        using var sent = JsonDocument.Parse(body!);
        var content = sent.RootElement.GetProperty("messages")[0].GetProperty("content");
        Assert.Equal("text", Assert.Single(content.EnumerateArray()).GetProperty("type").GetString());
        var format = sent.RootElement.GetProperty("output_config").GetProperty("format");
        Assert.Equal(["t1", "t2"], format.GetProperty("schema").GetProperty("properties").GetProperty("suggestions")
            .GetProperty("items").GetProperty("properties").GetProperty("tag").GetProperty("enum")
            .EnumerateArray().Select(code => code.GetString()));
        Assert.Equal("low", sent.RootElement.GetProperty("output_config").GetProperty("effort").GetString());
    }

    [Fact]
    public async Task EveryUnavailableCauseIsLoggedWithAnEventId()
    {
        // Switched off: the service logs a warning before refusing.
        await using var application = await TestApplication.CreateAsync();
        Guid productId;
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider;
            await provider.GetRequiredService<IPurchaseDraftService>().CreateAsync(
                new CreatePurchaseDraftCommand("Lidl", null, "PLN", null,
                    [new PurchaseItemCommand("CHLEB", 1, "szt", 4.00m, 4.00m)], AllowPossibleDuplicate: true),
                Cancel);
            await provider.GetRequiredService<IProductTagService>().CreateAsync("веган", Cancel);
            productId = provider.GetRequiredService<PurchaseMemoryDbContext>().Products.Single().Id;
        }

        await using (var scope = application.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider;
            var serviceLog = new CapturingLogger<ProductTagSuggestionService>();
            var service = new ProductTagSuggestionService(
                provider.GetRequiredService<IProductCatalogService>(),
                provider.GetRequiredService<IProductTagRepository>(),
                provider.GetRequiredService<IProductCategoryRepository>(),
                new FakeSuggester { Configured = false },
                serviceLog);

            await Assert.ThrowsAsync<TagSuggestionsUnavailableException>(() => service.SuggestAsync(productId, "ru", Cancel));
            var entry = Assert.Single(serviceLog.Entries);
            Assert.Equal((LogLevel.Warning, 3402), (entry.Level, entry.EventId));
        }

        // Inside the Claude suggester: no key, provider error and an unexpected exception each log 3412.
        var product = new TagSuggestionProduct("Chleb", null, null, [], []);
        TagSuggestionOption[] tags = [new(Guid.NewGuid(), "веган")];
        var withoutKeyLog = new CapturingLogger<AnthropicTagSuggester>();
        using var withoutKey = new AnthropicTagSuggester(Options.Create(new TagSuggestionOptions()), withoutKeyLog, null, () => null);
        await Assert.ThrowsAsync<TagSuggestionsUnavailableException>(
            () => withoutKey.SuggestAsync(product, tags, TagSuggestionLanguage.Russian, Cancel));
        Assert.Contains(withoutKeyLog.Entries, entry => entry.EventId == 3412 && entry.Message.Contains("not_configured", StringComparison.Ordinal));

        foreach (var failure in new Exception[]
                 {
                     new AnthropicServiceException(500, "boom", new InvalidOperationException()),
                     new InvalidOperationException("unexpected")
                 })
        {
            var log = new CapturingLogger<AnthropicTagSuggester>();
            using var suggester = new AnthropicTagSuggester(Options.Create(new TagSuggestionOptions()), log, new ThrowingTransport(failure), () => null);
            await Assert.ThrowsAsync<TagSuggestionsUnavailableException>(
                () => suggester.SuggestAsync(product, tags, TagSuggestionLanguage.Russian, Cancel));
            var entry = Assert.Single(log.Entries);
            Assert.Equal((LogLevel.Warning, 3412), (entry.Level, entry.EventId));
            Assert.DoesNotContain("unexpected\"", entry.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task BadTagSuggestionSettingsStopTheHostAtStartup()
    {
        var validator = new TagSuggestionOptionsValidator();
        Assert.True(validator.Validate(null, new TagSuggestionOptions()).Succeeded);
        Assert.True(validator.Validate(null, new TagSuggestionOptions { Enabled = false, Model = "" }).Succeeded);
        var failed = validator.Validate(null, new TagSuggestionOptions
        {
            Model = " ",
            RequestTimeoutSeconds = 0,
            MaximumRetries = -1,
            MaximumOutputTokens = 10,
            MaximumOfferedTags = 0
        });
        Assert.True(failed.Failed);
        Assert.Equal(5, failed.Failures!.Count());

        await using var host = new AuthTestHost(settings: new Dictionary<string, string?> { ["TagSuggestions:RequestTimeoutSeconds"] = "0" });
        var exception = Assert.ThrowsAny<Exception>(() => host.CreateClient());
        Assert.Contains("RequestTimeoutSeconds", exception.ToString(), StringComparison.Ordinal);
    }

    private static AnthropicTagSuggester CreateSuggester(IAnthropicMessagesTransport transport) =>
        new(Options.Create(new TagSuggestionOptions()), NullLogger<AnthropicTagSuggester>.Instance, transport, () => null);

    private static async Task<Guid> SeedProductAsync(AuthTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        await provider.GetRequiredService<IPurchaseDraftService>().CreateAsync(
            new CreatePurchaseDraftCommand(
                "Lidl", provider.GetRequiredService<TimeProvider>().GetUtcNow(), "PLN", null,
                [new PurchaseItemCommand("SOK JABLKOWY", 1, "szt", 6.00m, 6.00m)],
                AllowPossibleDuplicate: true),
            Cancel);
        return provider.GetRequiredService<PurchaseMemoryDbContext>().Products.Single().Id;
    }

    /// <summary>Three household tags; the product already carries «Акция».</summary>
    private static async Task<(Guid Kid, Guid Promo, Guid Vegan)> SeedTagsAsync(AuthTestHost host, Guid productId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var tags = scope.ServiceProvider.GetRequiredService<IProductTagService>();
        var kid = (await tags.CreateAsync("Для ребёнка", Cancel)).Id;
        var promo = (await tags.CreateAsync("Акция", Cancel)).Id;
        var vegan = (await tags.CreateAsync("Веган", Cancel)).Id;
        await tags.SetProductTagsAsync(productId, [promo], new ReceiptActor("user-anna", "anna@example.com"), Cancel);
        return (kid, promo, vegan);
    }

    private sealed class FakeSuggester : ITagSuggester
    {
        private int _calls;

        public bool Configured { get; init; } = true;

        public bool IsConfigured => Configured;

        public int Calls => _calls;

        public IReadOnlyList<SuggestedTag> Answer { get; set; } = [];

        public Exception? Failure { get; set; }

        public TagSuggestionProduct? LastProduct { get; private set; }

        public IReadOnlyList<TagSuggestionOption>? LastTags { get; private set; }

        public TagSuggestionLanguage? LastLanguage { get; private set; }

        public Task<IReadOnlyList<SuggestedTag>> SuggestAsync(
            TagSuggestionProduct product,
            IReadOnlyList<TagSuggestionOption> tags,
            TagSuggestionLanguage language,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            LastProduct = product;
            LastTags = tags;
            LastLanguage = language;
            return Failure is null ? Task.FromResult(Answer) : Task.FromException<IReadOnlyList<SuggestedTag>>(Failure);
        }
    }

    private sealed class RecordingTransport(AnthropicMessagesResult response) : IAnthropicMessagesTransport
    {
        public List<AnthropicMessagesRequest> Requests { get; } = [];

        public Task<AnthropicMessagesResult> CreateAsync(AnthropicMessagesRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingTransport(Exception exception) : IAnthropicMessagesTransport
    {
        public Task<AnthropicMessagesResult> CreateAsync(AnthropicMessagesRequest request, CancellationToken cancellationToken) =>
            Task.FromException<AnthropicMessagesResult>(exception);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, int EventId, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId.Id, formatter(state, exception)));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }
}

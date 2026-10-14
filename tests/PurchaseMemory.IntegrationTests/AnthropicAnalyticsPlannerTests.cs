using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Analytics;
using PurchaseMemory.Infrastructure.Analytics;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// The Claude analytics planner against a fake transport, the router that
/// falls back between planners, and the <c>Analytics:*</c> settings. No
/// network, no key, no cost.
/// </summary>
public sealed class AnthropicAnalyticsPlannerTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);
    private static readonly Guid Dairy = Guid.NewGuid();
    private static readonly Guid Milk = Guid.NewGuid();
    private static readonly Guid Kid = Guid.NewGuid();
    private static readonly AnalyticsVocabulary Vocabulary = new(
        [new AnalyticsCategory(Dairy, null, "Молочное и яйца", "dairy"), new AnalyticsCategory(Milk, Dairy, "Молоко", "milk")],
        ["Biedronka", "Lidl"],
        ["Łaciate"],
        [new AnalyticsTag(Kid, "Для ребёнка")]);

    private const string MilkInAugust =
        """{"understood":true,"metric":"averageUnitPrice","categories":["c2"],"nameTerms":["mleko"],"brands":["b1"],"merchants":["m1","m9"],"tags":[],"period":{"preset":"month","from":null,"to":null,"month":8,"year":null},"discount":"any","groupBy":"none","limit":null}""";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static AnalyticsPlanningRequest Question(string text = "Сколько стоило молоко в августе?") =>
        new(text, null, Today, Vocabulary);

    [Fact]
    public async Task SendsTheSharedPromptAndSchemaAndMapsTheAnswerBack()
    {
        var transport = new RecordingTransport(Completed(MilkInAugust));
        var result = await Create(transport).PlanAsync(Question(), Cancel);

        var request = Assert.Single(transport.Requests);
        Assert.Equal(("claude-haiku-5-5", AnthropicEffort.Low, 4000), (request.Model, request.Effort, request.MaximumOutputTokens));
        Assert.Empty(request.Images);
        var codes = new AnalyticsPlanContract.Codebook(Vocabulary);
        // Same contract as the OpenAI planner, so both produce the same plan shape.
        Assert.Equal(AnalyticsPlanContract.BuildPrompt(Question(), codes), request.Prompt);
        Assert.Equal(AnalyticsPlanContract.BuildSchema(codes), request.JsonSchema);
        Assert.DoesNotContain(Milk.ToString(), request.Prompt, StringComparison.Ordinal);

        Assert.True(result.Understood);
        Assert.Equal(AnalyticsMetric.AverageUnitPrice, result.Plan.Metric);
        Assert.Equal([Milk], result.Plan.CategoryIds);
        Assert.Equal(["mleko"], result.Plan.NameTerms);
        Assert.Equal(["Łaciate"], result.Plan.Brands);
        Assert.Equal(["Biedronka"], result.Plan.Merchants);
        Assert.Equal((new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), (result.Plan.From!.Value, result.Plan.To!.Value));
        Assert.Equal(new AnalyticsPeriodRequest(AnalyticsPeriodPreset.Month, null, null, 8, null), result.Period);
        Assert.Equal([(AnalyticsNote.UnknownMerchant, "m9")], result.Notes.Select(note => (note.Code, note.Value)));
        Assert.Equal((900L, 120L), (result.InputTokens!.Value, result.OutputTokens!.Value));
    }

    [Fact]
    public void ThePlanSchemaSurvivesTheClaudeAdapterUnchanged()
    {
        // Claude rejects numeric/length keywords; the plan schema has none, so
        // nothing is stripped — nullable types, enums and required lists stay.
        var many = Vocabulary with { Tags = Enumerable.Range(1, 150).Select(index => new AnalyticsTag(Guid.NewGuid(), $"Тег {index}")).ToList() };
        var schema = AnalyticsPlanContract.BuildSchema(new AnalyticsPlanContract.Codebook(many));
        using var original = JsonDocument.Parse(schema);
        using var adapted = JsonDocument.Parse(AnthropicJsonSchemaAdapter.AdaptToJson(schema));
        Assert.Equal(
            JsonSerializer.Serialize(original.RootElement),
            JsonSerializer.Serialize(adapted.RootElement));
        var properties = adapted.RootElement.GetProperty("properties");
        Assert.Equal(AnalyticsPlanContract.Codebook.MaximumTags, properties.GetProperty("tags").GetProperty("items").GetProperty("enum").GetArrayLength());
        Assert.Equal(JsonValueKind.False, properties.GetProperty("period").GetProperty("additionalProperties").ValueKind);
    }

    [Theory]
    [InlineData("max_tokens", null, """{"understood":true""")]
    [InlineData("refusal", "cyber", null)]
    [InlineData("end_turn", null, null)]
    [InlineData("end_turn", null, "not json")]
    [InlineData("end_turn", null, "[1,2]")]
    [InlineData("end_turn", null, """{"understood":true,"categories":[]}""")]
    public async Task IncompleteRefusedOrMalformedAnswersBecomePlanningErrors(string stopReason, string? refusal, string? json)
    {
        var planner = Create(new RecordingTransport(new AnthropicMessagesResult(json, stopReason, refusal, "msg", 10, 10)));
        var exception = await Assert.ThrowsAsync<AnalyticsPlanningException>(() => planner.PlanAsync(Question(), Cancel));
        Assert.IsNotType<AnalyticsQuestionNotUnderstoodException>(exception);
    }

    [Fact]
    public async Task ProviderAndUnexpectedFailuresBecomePlanningErrors()
    {
        var limited = await Assert.ThrowsAsync<AnalyticsPlanningException>(() =>
            Create(new ThrowingTransport(new AnthropicProviderException(429, "limited", new InvalidOperationException()))).PlanAsync(Question(), Cancel));
        Assert.Contains("rate_limited", limited.Message, StringComparison.Ordinal);

        var overloaded = await Assert.ThrowsAsync<AnalyticsPlanningException>(() =>
            Create(new ThrowingTransport(new AnthropicProviderException(529, "busy", new InvalidOperationException()))).PlanAsync(Question(), Cancel));
        Assert.Contains("overloaded", overloaded.Message, StringComparison.Ordinal);

        var timeout = await Assert.ThrowsAsync<AnalyticsPlanningException>(() =>
            Create(new ThrowingTransport(new TaskCanceledException("timed out"))).PlanAsync(Question(), Cancel));
        Assert.Contains("timeout", timeout.Message, StringComparison.Ordinal);

        var odd = await Assert.ThrowsAsync<AnalyticsPlanningException>(() =>
            Create(new ThrowingTransport(new NotSupportedException("sdk"))).PlanAsync(Question(), Cancel));
        Assert.Contains("unexpected:NotSupportedException", odd.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CallerCancellationIsNotAPlanningError()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Create(new ThrowingTransport(new OperationCanceledException(cancellation.Token))).PlanAsync(Question(), cancellation.Token));
    }

    [Fact]
    public async Task NotConfiguredWithoutKeyOrWhenSwitchedOff()
    {
        var noKey = new AnthropicAnalyticsQueryPlanner(Options.Create(new AnalyticsOptions()), NullLogger<AnthropicAnalyticsQueryPlanner>.Instance, null, () => null);
        Assert.False(noKey.IsConfigured);
        await Assert.ThrowsAsync<AnalyticsPlanningException>(() => noKey.PlanAsync(Question(), Cancel));
        Assert.False(new AnthropicAnalyticsQueryPlanner(Options.Create(new AnalyticsOptions { Enabled = false }), NullLogger<AnthropicAnalyticsQueryPlanner>.Instance, null, () => "key").IsConfigured);
        Assert.True(new AnthropicAnalyticsQueryPlanner(Options.Create(new AnalyticsOptions()), NullLogger<AnthropicAnalyticsQueryPlanner>.Instance, null, () => "key").IsConfigured);
        Assert.Equal("claude-haiku-5-5", noKey.ModelName);
    }

    [Fact]
    public async Task RouterFallsBackWhenThePrimaryFailsOrHasNoKey()
    {
        var answer = new AnalyticsPlanningResult(new AnalyticsPlan { Metric = AnalyticsMetric.Quantity }, true, [], 1, 1);
        var failing = new FakePlanner("claude") { Failure = new AnalyticsPlanningException("Claude did not answer (overloaded).") };
        var backup = new FakePlanner("luna") { Result = answer };
        var router = Router(("anthropic", failing), ("openai", backup));

        Assert.True(router.IsConfigured);
        Assert.Equal("claude", router.ModelName);
        Assert.Same(answer, await router.PlanAsync(Question(), Cancel));
        Assert.Equal((1, 1), (failing.Calls, backup.Calls));

        var noKey = new FakePlanner("claude") { Configured = false };
        var skipped = Router(("anthropic", noKey), ("openai", backup));
        Assert.Equal("luna", skipped.ModelName);
        Assert.Same(answer, await skipped.PlanAsync(Question(), Cancel));
        Assert.Equal(0, noKey.Calls);
    }

    [Fact]
    public async Task RouterNeverRetriesAnAnswerElsewhere()
    {
        var notAboutPurchases = new AnalyticsPlanningResult(new AnalyticsPlan(), false, [], 1, 1);
        var primary = new FakePlanner("claude") { Result = notAboutPurchases };
        var backup = new FakePlanner("luna") { Result = new AnalyticsPlanningResult(new AnalyticsPlan(), true, [], 1, 1) };
        Assert.Same(notAboutPurchases, await Router(("anthropic", primary), ("openai", backup)).PlanAsync(Question(), Cancel));
        Assert.Equal(0, backup.Calls);
    }

    [Fact]
    public async Task RouterReportsTheLastFailureOrThatNothingIsConfigured()
    {
        var last = new AnalyticsPlanningException("OpenAI analytics planning request failed.");
        var router = Router(
            ("anthropic", new FakePlanner("claude") { Failure = new AnalyticsPlanningException("first") }),
            ("openai", new FakePlanner("luna") { Failure = last }));
        Assert.Same(last, await Assert.ThrowsAsync<AnalyticsPlanningException>(() => router.PlanAsync(Question(), Cancel)));

        var nothing = Router(("anthropic", new FakePlanner("claude") { Configured = false }));
        Assert.False(nothing.IsConfigured);
        await Assert.ThrowsAsync<AnalyticsPlanningException>(() => nothing.PlanAsync(Question(), Cancel));
    }

    [Fact]
    public void SettingsRejectUnknownOrRepeatedProvidersAndBadLimits()
    {
        var validator = new AnalyticsOptionsValidator();
        Assert.True(validator.Validate(null, new AnalyticsOptions()).Succeeded);
        Assert.True(validator.Validate(null, new AnalyticsOptions { Provider = " Anthropic ", FallbackProviders = ["openai"] }).Succeeded);
        Assert.Equal(["anthropic", "openai"], new AnalyticsOptions { Provider = " Anthropic ", FallbackProviders = ["OpenAI"] }.PlannerOrder());
        Assert.False(new AnalyticsOptions().HasFallback);
        Assert.True(new AnalyticsOptions { FallbackProviders = ["anthropic"] }.HasFallback);

        var failed = validator.Validate(null, new AnalyticsOptions
        {
            Provider = "gemini",
            FallbackProviders = ["openai", "OPENAI"],
            AnthropicEffort = (AnthropicEffort)42,
            RequestTimeoutSeconds = 0,
            AnthropicMaximumRetries = 9,
            AnthropicMaximumOutputTokens = 10,
            OpenAiMaximumOutputTokens = 10
        });
        Assert.True(failed.Failed);
        Assert.Equal(7, failed.Failures!.Count());

        var noModel = validator.Validate(null, new AnalyticsOptions { Provider = "anthropic", AnthropicModel = " " });
        Assert.Contains("anthropic", Assert.Single(noModel.Failures!), StringComparison.Ordinal);
        Assert.True(validator.Validate(null, new AnalyticsOptions { Enabled = false, Provider = "anthropic", AnthropicModel = " " }).Succeeded);
    }

    [Fact]
    public async Task TheHostWiresTheConfiguredOrderAndRejectsBadSettingsAtStartup()
    {
        await using (var host = new AuthTestHost(settings: new Dictionary<string, string?>
        {
            ["Analytics:Provider"] = "anthropic",
            ["Analytics:FallbackProviders:0"] = "openai"
        }))
        {
            var planner = host.Services.GetRequiredService<IAnalyticsQueryPlanner>();
            Assert.IsType<AnalyticsQueryPlannerRouter>(planner);
            // Analytics:Enabled is false in tests, so no planner may call a model.
            Assert.False(planner.IsConfigured);
            Assert.Equal("claude-haiku-5-5", planner.ModelName);
        }

        await using var bad = new AuthTestHost(settings: new Dictionary<string, string?> { ["Analytics:Provider"] = "gemini" });
        var exception = Assert.ThrowsAny<Exception>(() => bad.CreateClient());
        Assert.Contains("Analytics:Provider", exception.ToString(), StringComparison.Ordinal);
    }

    private static AnthropicAnalyticsQueryPlanner Create(IAnthropicMessagesTransport transport) =>
        new(Options.Create(new AnalyticsOptions()), NullLogger<AnthropicAnalyticsQueryPlanner>.Instance, transport, () => null);

    private static AnalyticsQueryPlannerRouter Router(params (string Provider, IAnalyticsQueryPlanner Planner)[] planners) =>
        new(planners, NullLogger<AnalyticsQueryPlannerRouter>.Instance);

    private static AnthropicMessagesResult Completed(string json) => new(json, "end_turn", null, "msg_1", 900, 120);

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

    private sealed class FakePlanner(string model) : IAnalyticsQueryPlanner
    {
        public bool Configured { get; init; } = true;

        public AnalyticsPlanningResult? Result { get; init; }

        public AnalyticsPlanningException? Failure { get; init; }

        public int Calls { get; private set; }

        public bool IsConfigured => Configured;

        public string ModelName => model;

        public Task<AnalyticsPlanningResult> PlanAsync(AnalyticsPlanningRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return Failure is not null ? Task.FromException<AnalyticsPlanningResult>(Failure) : Task.FromResult(Result!);
        }
    }
}

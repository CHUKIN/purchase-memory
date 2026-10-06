using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Analytics;

namespace PurchaseMemory.IntegrationTests;

/// <summary>The analytics endpoints through the real host, with real auth and a fake planner.</summary>
public sealed class AnalyticsApiTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> ProtectedCalls => new()
    {
        { "GET", "/api/analytics/context" },
        { "POST", "/api/analytics/ask" },
        { "POST", "/api/analytics/run" }
    };

    [Theory]
    [MemberData(nameof(ProtectedCalls))]
    public async Task EveryAnalyticsCallNeedsAnApprovedValidToken(string method, string path)
    {
        var planner = new FakePlanner();
        await using var host = new AuthTestHost(configureServices: services => services.AddSingleton<IAnalyticsQueryPlanner>(planner));
        foreach (var token in new[]
                 {
                     null,
                     AuthTestHost.Token(key: AuthTestHost.ForeignKey),
                     AuthTestHost.Token(expires: DateTime.UtcNow.AddMinutes(-5))
                 })
        {
            using var client = host.CreateClientFor(token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Request(method, path), Cancel)).StatusCode);
        }

        using var pending = host.CreateClientFor(AuthTestHost.Token(access: "pending"));
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.SendAsync(Request(method, path), Cancel)).StatusCode);
        Assert.Equal(0, planner.Calls);
    }

    [Fact]
    public async Task DisabledQuestionsAreRefusedButPlansStillRun()
    {
        // The test host switches Analytics off, and there is no key: the real planner is not configured.
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var context = await client.GetFromJsonAsync<JsonElement>("/api/analytics/context", Cancel);
        Assert.False(context.GetProperty("enabled").GetBoolean());
        Assert.Equal(0, context.GetProperty("merchants").GetArrayLength());

        var ask = await client.PostAsJsonAsync("/api/analytics/ask", new { question = "Сколько потратили?" }, Cancel);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ask.StatusCode);
        Assert.Equal("analyticsDisabled", (await ask.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());

        var run = await client.PostAsJsonAsync("/api/analytics/run", new
        {
            plan = new { metric = "spend", from = "2026-01-01", categoryIds = new[] { Guid.NewGuid() } },
            locale = "en"
        }, Cancel);
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var answer = await run.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("No purchases match these filters.", answer.GetProperty("conclusion").GetString());
        Assert.False(answer.GetProperty("result").GetProperty("hasData").GetBoolean());
        Assert.Equal("unknownCategory", answer.GetProperty("notes")[0].GetProperty("code").GetString());
        Assert.Equal("spend", answer.GetProperty("plan").GetProperty("metric").GetString());

        var badEnum = await client.PostAsJsonAsync("/api/analytics/run", new { plan = new { metric = "everything" } }, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, badEnum.StatusCode);
        var empty = await client.PostAsJsonAsync("/api/analytics/run", new { locale = "ru" }, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task AskRunsThePlannedQueryAndIsRateLimitedPerUser()
    {
        var planner = new FakePlanner();
        await using var host = new AuthTestHost(
            settings: new Dictionary<string, string?> { ["RateLimiting:AnalyticsQuestions:TokenLimit"] = "2" },
            configureServices: services => services.AddSingleton<IAnalyticsQueryPlanner>(planner));
        using var anna = host.CreateClientFor(AuthTestHost.Token());

        var first = await anna.PostAsJsonAsync("/api/analytics/ask", new { question = "Сколько потратили в этом году?" }, Cancel);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var answer = await first.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("По этим условиям покупок не нашлось.", answer.GetProperty("conclusion").GetString());
        Assert.Equal("month", answer.GetProperty("plan").GetProperty("groupBy").GetString());

        var followUp = await anna.PostAsJsonAsync("/api/analytics/ask", new
        {
            question = "а по магазинам?",
            previousPlan = answer.GetProperty("plan")
        }, Cancel);
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
        Assert.NotNull(planner.LastRequest!.PreviousPlan);
        Assert.Equal(AnalyticsGroupBy.Month, planner.LastRequest.PreviousPlan!.GroupBy);

        var limited = await anna.PostAsJsonAsync("/api/analytics/ask", new { question = "ещё?" }, Cancel);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter is not null);
        Assert.Equal("Too many questions", (await limited.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("title").GetString());
        Assert.Equal(2, planner.Calls);

        // Another user has a bucket of their own, and running an edited plan is never limited.
        using var boris = host.CreateClientFor(AuthTestHost.Token(subject: "22222222-2222-2222-2222-222222222222"));
        Assert.Equal(HttpStatusCode.OK, (await boris.PostAsJsonAsync("/api/analytics/ask", new { question = "?" }, Cancel)).StatusCode);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.Equal(HttpStatusCode.OK, (await anna.PostAsJsonAsync("/api/analytics/run", new { plan = new { metric = "spend" } }, Cancel)).StatusCode);
        }
    }

    [Fact]
    public async Task RunHasAGenerousLimitOfItsOwn()
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token());
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 121; attempt++)
        {
            statuses.Add((await client.PostAsJsonAsync("/api/analytics/run", new { plan = new { metric = "spend" } }, Cancel)).StatusCode);
        }

        Assert.All(statuses.Take(120), status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    [Fact]
    public async Task PlannerFailuresAreGracefulProblems()
    {
        var planner = new FakePlanner();
        await using var host = new AuthTestHost(configureServices: services => services.AddSingleton<IAnalyticsQueryPlanner>(planner));
        using var client = host.CreateClientFor(AuthTestHost.Token());

        planner.Failure = new AnalyticsPlanningException("OpenAI returned malformed plan JSON.");
        var failed = await client.PostAsJsonAsync("/api/analytics/ask", new { question = "Сколько?" }, Cancel);
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
        var problem = await failed.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("planningFailed", problem.GetProperty("code").GetString());
        Assert.DoesNotContain("malformed", problem.GetRawText(), StringComparison.Ordinal);

        planner.Failure = null;
        planner.Understood = false;
        var weather = await client.PostAsJsonAsync("/api/analytics/ask", new { question = "Какая погода?" }, Cancel);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, weather.StatusCode);

        var tooLong = await client.PostAsJsonAsync("/api/analytics/ask", new { question = new string('x', 501) }, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        var blank = await client.PostAsJsonAsync("/api/analytics/ask", new { question = "  " }, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
    }

    [Fact]
    public async Task McpRunsAPlanWithoutTheModel()
    {
        var planner = new FakePlanner();
        await using var host = new AuthTestHost(configureServices: services => services.AddSingleton<IAnalyticsQueryPlanner>(planner));
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var list = await PostMcpAsync(client, """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""");
        Assert.Contains("run_analytics_plan", await list.Content.ReadAsStringAsync(Cancel), StringComparison.Ordinal);

        var call = await PostMcpAsync(client,
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"run_analytics_plan","arguments":{"plan":{"metric":"exists","nameTerms":["mleko"]},"locale":"en"}}}""");
        var body = await call.Content.ReadAsStringAsync(Cancel);
        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        Assert.Contains("No, no such purchases were found.", body, StringComparison.Ordinal);
        Assert.Equal(0, planner.Calls);
    }

    [Fact]
    public async Task RunFiltersAndBreaksDownByTagWithoutTheModel()
    {
        await using var host = new AuthTestHost();
        var (kid, promo) = await SeedTaggedPurchaseAsync(host);
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var filtered = await client.PostAsJsonAsync("/api/analytics/run", new
        {
            plan = new { metric = "spend", tagIds = new[] { kid, Guid.NewGuid() } },
            locale = "ru"
        }, Cancel);
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        var answer = await filtered.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal([kid.ToString()], answer.GetProperty("plan").GetProperty("tagIds").EnumerateArray().Select(id => id.GetString()));
        Assert.Equal("unknownTag", Assert.Single(answer.GetProperty("notes").EnumerateArray()).GetProperty("code").GetString());
        Assert.Equal(6.00m, answer.GetProperty("result").GetProperty("kpis")[0].GetProperty("value").GetDecimal());
        Assert.EndsWith("Учтены только товары с тегом «Для ребёнка».", answer.GetProperty("conclusion").GetString(), StringComparison.Ordinal);

        var byTag = await client.PostAsJsonAsync("/api/analytics/run", new { plan = new { metric = "spend", groupBy = "tag" }, locale = "en" }, Cancel);
        Assert.Equal(HttpStatusCode.OK, byTag.StatusCode);
        var breakdown = await byTag.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        var result = breakdown.GetProperty("result");
        Assert.Equal("tag", result.GetProperty("groupBy").GetString());
        // Juice (6) carries both tags, bread (4) none: 6 + 6 + 4 bars over a 10 total.
        Assert.Equal(
            [(promo.ToString(), "Акция", 6.00m), (kid.ToString(), "Для ребёнка", 6.00m), ("", null, 4.00m)],
            result.GetProperty("groups").EnumerateArray().Select(group => (
                group.GetProperty("key").GetString(),
                group.GetProperty("label").GetString(),
                group.GetProperty("value").GetDecimal())));
        Assert.Equal(promo.ToString(), result.GetProperty("groups")[0].GetProperty("tagId").GetString());
        Assert.Equal(1, result.GetProperty("coverage").GetProperty("multiTaggedLines").GetInt32());
        Assert.DoesNotContain("tag", breakdown.GetProperty("conclusion").GetString(), StringComparison.OrdinalIgnoreCase);

        var badGroup = await client.PostAsJsonAsync("/api/analytics/run", new { plan = new { metric = "spend", groupBy = "tags" } }, Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, badGroup.StatusCode);
        var badId = await client.PostAsync("/api/analytics/run", JsonContent.Create(
            JsonDocument.Parse("""{"plan":{"metric":"spend","tagIds":["детское"]}}""").RootElement), Cancel);
        Assert.Equal(HttpStatusCode.BadRequest, badId.StatusCode);
    }

    [Fact]
    public async Task AskGivesThePlannerTheTagsAndRunsItsTagPlan()
    {
        var planner = new FakePlanner();
        await using var host = new AuthTestHost(configureServices: services => services.AddSingleton<IAnalyticsQueryPlanner>(planner));
        var (kid, _) = await SeedTaggedPurchaseAsync(host);
        planner.TagIds = [kid];
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var asked = await client.PostAsJsonAsync("/api/analytics/ask", new { question = "Сколько потратили на детское?", locale = "en" }, Cancel);
        Assert.Equal(HttpStatusCode.OK, asked.StatusCode);
        Assert.Equal(
            [("Акция", false), ("Для ребёнка", true)],
            planner.LastRequest!.Vocabulary.Tags.Select(tag => (tag.Name, tag.Id == kid)));
        var answer = await asked.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(kid.ToString(), answer.GetProperty("plan").GetProperty("tagIds")[0].GetString());
        Assert.EndsWith("Only products tagged «Для ребёнка» are counted.", answer.GetProperty("conclusion").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, planner.Calls);
    }

    [Fact]
    public async Task McpRunsATagPlanAndDescribesTags()
    {
        var planner = new FakePlanner();
        await using var host = new AuthTestHost(configureServices: services => services.AddSingleton<IAnalyticsQueryPlanner>(planner));
        var (kid, _) = await SeedTaggedPurchaseAsync(host);
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var list = await (await PostMcpAsync(client, """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""")).Content.ReadAsStringAsync(Cancel);
        Assert.Contains("tagIds (ids from list_tags", list, StringComparison.Ordinal);
        Assert.Contains("coverage.multiTaggedLines", list, StringComparison.Ordinal);

        var call = await PostMcpAsync(client,
            """{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"run_analytics_plan","arguments":{"plan":{"metric":"spend","tagIds":["KID"],"groupBy":"tag"},"locale":"en"}}}"""
                .Replace("KID", kid.ToString(), StringComparison.Ordinal));
        var body = await call.Content.ReadAsStringAsync(Cancel);
        Assert.Equal(HttpStatusCode.OK, call.StatusCode);
        Assert.Contains("Spent 6.00", body, StringComparison.Ordinal);
        Assert.Contains("Only products tagged", body, StringComparison.Ordinal);
        // Text-only clients learn about the overlap from the coverage field and the tool description.
        Assert.Contains("multiTaggedLines", body, StringComparison.Ordinal);
        Assert.Contains(kid.ToString(), body, StringComparison.Ordinal);
        Assert.Equal(0, planner.Calls);
    }

    /// <summary>One receipt today: juice (6 zł) tagged «Для ребёнка» and «Акция», bread (4 zł) untagged.</summary>
    private static async Task<(Guid Kid, Guid Promo)> SeedTaggedPurchaseAsync(AuthTestHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        await provider.GetRequiredService<PurchaseMemory.Application.Purchases.IPurchaseDraftService>().CreateAsync(
            new PurchaseMemory.Application.Purchases.CreatePurchaseDraftCommand(
                "Lidl", provider.GetRequiredService<TimeProvider>().GetUtcNow(), "PLN", null,
                [
                    new PurchaseMemory.Application.Purchases.PurchaseItemCommand("SOK JABLKOWY", 1, "szt", 6.00m, 6.00m),
                    new PurchaseMemory.Application.Purchases.PurchaseItemCommand("CHLEB", 1, "szt", 4.00m, 4.00m)
                ],
                AllowPossibleDuplicate: true),
            Cancel);
        var tags = provider.GetRequiredService<PurchaseMemory.Application.Products.Tags.IProductTagService>();
        var kid = (await tags.CreateAsync("Для ребёнка", Cancel)).Id;
        var promo = (await tags.CreateAsync("Акция", Cancel)).Id;
        var context = provider.GetRequiredService<PurchaseMemory.Infrastructure.Persistence.PurchaseMemoryDbContext>();
        var juice = context.Products.Single(product => product.NormalizedName.StartsWith("sok")).Id;
        await tags.SetProductTagsAsync(juice, [kid, promo], new PurchaseMemory.Application.Receipts.ReceiptActor("user-anna", "anna@example.com"), Cancel);
        return (kid, promo);
    }

    private static Task<HttpResponseMessage> PostMcpAsync(HttpClient client, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return client.SendAsync(request, Cancel);
    }

    private static HttpRequestMessage Request(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { question = "?", plan = new { metric = "spend" } });
        }

        return request;
    }

    private sealed class FakePlanner : IAnalyticsQueryPlanner
    {
        private int _calls;

        public bool IsConfigured => true;

        public string ModelName => "fake";

        public int Calls => _calls;

        public AnalyticsPlanningRequest? LastRequest { get; private set; }

        public Exception? Failure { get; set; }

        public bool Understood { get; set; } = true;

        public IReadOnlyList<Guid> TagIds { get; set; } = [];

        public Task<AnalyticsPlanningResult> PlanAsync(AnalyticsPlanningRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            LastRequest = request;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(new AnalyticsPlanningResult(
                new AnalyticsPlan { Metric = AnalyticsMetric.Spend, From = new DateOnly(request.Today.Year, 1, 1), GroupBy = AnalyticsGroupBy.Month, TagIds = TagIds },
                Understood,
                [],
                10,
                5));
        }
    }
}

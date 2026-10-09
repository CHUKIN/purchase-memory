using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PurchaseMemory.Application.Analytics;
using PurchaseMemory.Domain.Analytics;
using PurchaseMemory.Infrastructure.Analytics;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>The analytics question history through the real host, with real auth and a fake planner.</summary>
public sealed class AnalyticsHistoryTests
{
    private const string Boris = "22222222-2222-2222-2222-222222222222";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> HistoryCalls => new()
    {
        { "GET", "/api/analytics/history" },
        { "GET", $"/api/analytics/history/{Guid.NewGuid()}" },
        { "POST", $"/api/analytics/history/{Guid.NewGuid()}/refresh" },
        { "DELETE", $"/api/analytics/history/{Guid.NewGuid()}" }
    };

    [Theory]
    [InlineData("  Сколько   потратили\tв Lidl?  ", "сколько потратили в lidl?")]
    [InlineData("MLEKO\n\n3,2%", "mleko 3,2%")]
    [InlineData("Żabka ŁÓDŹ", "żabka łódź")]
    public void QuestionsAreComparedTrimmedCaseInsensitiveAndWithCollapsedWhitespace(string question, string expected) =>
        Assert.Equal(expected, AnalyticsQuestionEntry.Normalize(question));

    [Theory]
    [MemberData(nameof(HistoryCalls))]
    public async Task EveryHistoryCallNeedsAnApprovedValidToken(string method, string path)
    {
        var planner = new FakePlanner();
        await using var host = Host(planner);
        foreach (var token in new[]
                 {
                     null,
                     AuthTestHost.Token(key: AuthTestHost.ForeignKey),
                     AuthTestHost.Token(expires: DateTime.UtcNow.AddMinutes(-5))
                 })
        {
            using var client = host.CreateClientFor(token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Cancel)).StatusCode);
        }

        using var pending = host.CreateClientFor(AuthTestHost.Token(access: "pending"));
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Cancel)).StatusCode);
        Assert.Equal(0, planner.Calls);
    }

    [Fact]
    public async Task AQuestionIsSavedAndTheSameWordsReuseItsPlanWithoutTheModel()
    {
        var planner = new FakePlanner();
        await using var host = Host(planner);
        using var anna = host.CreateClientFor(AuthTestHost.Token());

        var first = await AskAsync(anna, new { question = "Сколько потратили в этом году?", locale = "ru" });
        Assert.False(first.GetProperty("reused").GetBoolean());
        Assert.Equal("По этим условиям покупок не нашлось.", first.GetProperty("conclusion").GetString());
        var id = first.GetProperty("historyId").GetString();
        Assert.NotNull(id);
        Assert.Equal(1, planner.Calls);

        var list = await anna.GetFromJsonAsync<JsonElement>("/api/analytics/history", Cancel);
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(id, item.GetProperty("id").GetString());
        Assert.Equal("Сколько потратили в этом году?", item.GetProperty("question").GetString());
        Assert.Equal("По этим условиям покупок не нашлось.", item.GetProperty("conclusion").GetString());
        Assert.Equal("anna@example.com", item.GetProperty("askedByEmail").GetString());
        Assert.True(item.GetProperty("askedByMe").GetBoolean());
        Assert.False(item.GetProperty("isFollowUp").GetBoolean());
        Assert.False(list.GetProperty("hasMore").GetBoolean());
        Assert.False(item.TryGetProperty("askedByUserId", out _));

        var detail = await anna.GetFromJsonAsync<JsonElement>($"/api/analytics/history/{id}", Cancel);
        Assert.Equal("month", detail.GetProperty("answer").GetProperty("plan").GetProperty("groupBy").GetString());
        Assert.Equal("По этим условиям покупок не нашлось.", detail.GetProperty("answer").GetProperty("conclusion").GetString());
        Assert.Equal(id, detail.GetProperty("entry").GetProperty("id").GetString());

        // Other case and spacing are the same question: the saved plan runs, the model is not asked.
        var repeated = await AskAsync(anna, new { question = "  сколько ПОТРАТИЛИ   в этом году? ", locale = "en" });
        Assert.True(repeated.GetProperty("reused").GetBoolean());
        Assert.Equal(id, repeated.GetProperty("historyId").GetString());
        Assert.Equal("No purchases match these filters.", repeated.GetProperty("conclusion").GetString());
        Assert.Equal(1, planner.Calls);

        list = await anna.GetFromJsonAsync<JsonElement>("/api/analytics/history", Cancel);
        item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal("сколько ПОТРАТИЛИ   в этом году?", item.GetProperty("question").GetString());
        Assert.Equal("No purchases match these filters.", item.GetProperty("conclusion").GetString());

        // «Спросить заново» goes to the model on purpose and still keeps one entry.
        var fresh = await AskAsync(anna, new { question = "Сколько потратили в этом году?", askModel = true });
        Assert.False(fresh.GetProperty("reused").GetBoolean());
        Assert.Equal(id, fresh.GetProperty("historyId").GetString());
        Assert.Equal(2, planner.Calls);
        Assert.Single((await anna.GetFromJsonAsync<JsonElement>("/api/analytics/history", Cancel)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task TheHistoryBelongsToTheHouseholdAndShowsWhoAskedLast()
    {
        var planner = new FakePlanner();
        await using var host = Host(planner);
        using var anna = host.CreateClientFor(AuthTestHost.Token());
        using var boris = host.CreateClientFor(AuthTestHost.Token(subject: Boris, email: "boris@example.com"));

        var asked = await AskAsync(anna, new { question = "Сколько кофе?" });
        var id = asked.GetProperty("historyId").GetString();

        // Data is shared by every approved user in this version, and so is the history.
        var seenByBoris = Assert.Single((await boris.GetFromJsonAsync<JsonElement>("/api/analytics/history", Cancel))
            .GetProperty("items").EnumerateArray());
        Assert.Equal(id, seenByBoris.GetProperty("id").GetString());
        Assert.False(seenByBoris.GetProperty("askedByMe").GetBoolean());
        Assert.Equal("anna@example.com", seenByBoris.GetProperty("askedByEmail").GetString());

        var repeated = await AskAsync(boris, new { question = "сколько кофе?" });
        Assert.True(repeated.GetProperty("reused").GetBoolean());
        Assert.Equal(1, planner.Calls);
        var seenByAnna = Assert.Single((await anna.GetFromJsonAsync<JsonElement>("/api/analytics/history", Cancel))
            .GetProperty("items").EnumerateArray());
        Assert.Equal("boris@example.com", seenByAnna.GetProperty("askedByEmail").GetString());
        Assert.False(seenByAnna.GetProperty("askedByMe").GetBoolean());
    }

    [Fact]
    public async Task FollowUpsAreSavedButTheirWordsAloneNeverSelectAPlan()
    {
        var planner = new FakePlanner();
        await using var host = Host(planner);
        using var anna = host.CreateClientFor(AuthTestHost.Token());

        var first = await AskAsync(anna, new { question = "Сколько потратили в этом году?" });
        var previousPlan = first.GetProperty("plan");

        planner.GroupBy = AnalyticsGroupBy.Merchant;
        await Task.Delay(5, Cancel);
        var followUp = await AskAsync(anna, new { question = "а по магазинам?", previousPlan });
        Assert.False(followUp.GetProperty("reused").GetBoolean());
        Assert.Equal(2, planner.Calls);

        // The same follow-up on the same answer goes to the model (it depends on that answer)
        // but lands on the same entry when the plan comes out the same.
        await Task.Delay(5, Cancel);
        var again = await AskAsync(anna, new { question = "А по магазинам?", previousPlan });
        Assert.False(again.GetProperty("reused").GetBoolean());
        Assert.Equal(followUp.GetProperty("historyId").GetString(), again.GetProperty("historyId").GetString());
        Assert.Equal(3, planner.Calls);

        // A standalone question with a follow-up's words is a new question for the model.
        await Task.Delay(5, Cancel);
        var standalone = await AskAsync(anna, new { question = "а по магазинам?" });
        Assert.False(standalone.GetProperty("reused").GetBoolean());
        Assert.Equal(4, planner.Calls);

        var items = (await anna.GetFromJsonAsync<JsonElement>("/api/analytics/history", Cancel)).GetProperty("items");
        Assert.Equal(
            [("а по магазинам?", false), ("А по магазинам?", true), ("Сколько потратили в этом году?", false)],
            items.EnumerateArray().Select(item => (item.GetProperty("question").GetString(), item.GetProperty("isFollowUp").GetBoolean())));
    }

    [Fact]
    public async Task RefreshRecalculatesWithoutTheModelAndDeleteRemovesTheEntry()
    {
        var planner = new FakePlanner();
        await using var host = Host(planner);
        using var anna = host.CreateClientFor(AuthTestHost.Token());
        var id = (await AskAsync(anna, new { question = "Сколько потратили?" })).GetProperty("historyId").GetString();
        var before = (await anna.GetFromJsonAsync<JsonElement>($"/api/analytics/history/{id}", Cancel))
            .GetProperty("entry").GetProperty("answeredAt").GetDateTimeOffset();

        await Task.Delay(20, Cancel);
        var refreshed = await anna.PostAsync($"/api/analytics/history/{id}/refresh?locale=en", null, Cancel);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var detail = await refreshed.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("No purchases match these filters.", detail.GetProperty("answer").GetProperty("conclusion").GetString());
        Assert.True(detail.GetProperty("entry").GetProperty("answeredAt").GetDateTimeOffset() > before);
        Assert.Equal(1, planner.Calls);

        // Without a locale the answer is in Russian.
        var russian = await (await anna.PostAsync($"/api/analytics/history/{id}/refresh", null, Cancel)).Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("По этим условиям покупок не нашлось.", russian.GetProperty("answer").GetProperty("conclusion").GetString());
        Assert.Equal(1, planner.Calls);

        Assert.Equal(HttpStatusCode.NoContent, (await anna.DeleteAsync($"/api/analytics/history/{id}", Cancel)).StatusCode);
        foreach (var response in new[]
                 {
                     await anna.GetAsync($"/api/analytics/history/{id}", Cancel),
                     await anna.DeleteAsync($"/api/analytics/history/{id}", Cancel),
                     await anna.PostAsync($"/api/analytics/history/{id}/refresh?locale=ru", null, Cancel),
                     await anna.GetAsync($"/api/analytics/history/{Guid.NewGuid()}", Cancel)
                 })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("historyEntryNotFound", (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());
        }

        Assert.Empty((await anna.GetFromJsonAsync<JsonElement>("/api/analytics/history", Cancel)).GetProperty("items").EnumerateArray());

        // After deleting, the same words go to the model again.
        await AskAsync(anna, new { question = "Сколько потратили?" });
        Assert.Equal(2, planner.Calls);
    }

    [Fact]
    public async Task AnEntryAnOlderVersionWroteUnreadablyIsShownWithoutAnAnswerAndRefusesARefresh()
    {
        var planner = new FakePlanner();
        await using var host = Host(planner);
        using var anna = host.CreateClientFor(AuthTestHost.Token());
        var id = Guid.NewGuid();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAnalyticsHistoryRepository>();
            repository.Add(AnalyticsQuestionEntry.Create(
                id, "Старый вопрос?", false, "not json", null, "not json either", "Старый ответ", "user-anna", null, DateTimeOffset.UtcNow));
            await repository.SaveChangesAsync(Cancel);
        }

        var detail = await anna.GetFromJsonAsync<JsonElement>($"/api/analytics/history/{id}", Cancel);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("answer").ValueKind);
        Assert.Equal("Старый вопрос?", detail.GetProperty("entry").GetProperty("question").GetString());

        var refreshed = await anna.PostAsync($"/api/analytics/history/{id}/refresh?locale=ru", null, Cancel);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refreshed.StatusCode);
        Assert.Equal("historyEntryOutdated", (await refreshed.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());

        // It can still be deleted, and asking its words again plans afresh.
        var asked = await AskAsync(anna, new { question = "старый вопрос?" });
        Assert.False(asked.GetProperty("reused").GetBoolean());
        Assert.Equal(1, planner.Calls);
        Assert.Equal(HttpStatusCode.NoContent, (await anna.DeleteAsync($"/api/analytics/history/{id}", Cancel)).StatusCode);
    }

    [Fact]
    public async Task AReusedAnswerStillArrivesWhenTheHistoryCannotBeWritten()
    {
        var planner = new FakePlanner();
        var failing = new FailingSaves();
        await using var host = Host(planner, services => services.AddScoped<IAnalyticsHistoryRepository>(provider =>
            new FailingHistoryRepository(ActivatorUtilities.CreateInstance<AnalyticsHistoryRepository>(provider), failing)));
        using var anna = host.CreateClientFor(AuthTestHost.Token());
        var id = (await AskAsync(anna, new { question = "Сколько потратили?" })).GetProperty("historyId").GetString();
        Assert.NotNull(id);

        failing.Enabled = true;
        var repeated = await AskAsync(anna, new { question = "Сколько потратили?", locale = "en" });
        Assert.True(repeated.GetProperty("reused").GetBoolean());
        Assert.Equal(JsonValueKind.Null, repeated.GetProperty("historyId").ValueKind);
        Assert.Equal("No purchases match these filters.", repeated.GetProperty("conclusion").GetString());
        Assert.Equal(1, planner.Calls);
    }

    [Fact]
    public async Task TheListIsNewestFirstAndPaged()
    {
        var planner = new FakePlanner();
        await using var host = Host(planner);
        using var anna = host.CreateClientFor(AuthTestHost.Token());
        foreach (var question in new[] { "Первый?", "Второй?", "Третий?" })
        {
            await AskAsync(anna, new { question });
            await Task.Delay(5, Cancel);
        }

        var page = await anna.GetFromJsonAsync<JsonElement>("/api/analytics/history?limit=2", Cancel);
        Assert.Equal(["Третий?", "Второй?"], page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("question").GetString()));
        Assert.True(page.GetProperty("hasMore").GetBoolean());
        var next = await anna.GetFromJsonAsync<JsonElement>("/api/analytics/history?offset=2&limit=2", Cancel);
        Assert.Equal(["Первый?"], next.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("question").GetString()));
        Assert.False(next.GetProperty("hasMore").GetBoolean());

        // Asking an older question again moves it to the top.
        await AskAsync(anna, new { question = "первый?" });
        var top = await anna.GetFromJsonAsync<JsonElement>("/api/analytics/history?limit=1", Cancel);
        Assert.Equal("первый?", top.GetProperty("items")[0].GetProperty("question").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await anna.GetAsync("/api/analytics/history?limit=0", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anna.GetAsync("/api/analytics/history?limit=101", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anna.GetAsync("/api/analytics/history?offset=-1", Cancel)).StatusCode);
    }

    [Fact]
    public async Task ASavedQuestionStillAnswersWhenQuestionsAreSwitchedOff()
    {
        var planner = new FakePlanner();
        await using var host = Host(planner);
        using var anna = host.CreateClientFor(AuthTestHost.Token());
        await AskAsync(anna, new { question = "Сколько потратили?" });

        planner.Configured = false;
        var repeated = await AskAsync(anna, new { question = "Сколько потратили?" });
        Assert.True(repeated.GetProperty("reused").GetBoolean());

        foreach (var body in new object[] { new { question = "Что-то новое?" }, new { question = "Сколько потратили?", askModel = true } })
        {
            var refused = await anna.PostAsJsonAsync("/api/analytics/ask", body, Cancel);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            Assert.Equal("analyticsDisabled", (await refused.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("code").GetString());
        }

        Assert.Equal(1, planner.Calls);
    }

    [Fact]
    public async Task ANamedPeriodIsCountedFromTheNewTodayWhenTheQuestionComesBack()
    {
        var planner = new FakePlanner { Period = new AnalyticsPeriodRequest(AnalyticsPeriodPreset.ThisMonth) };
        var clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero));
        await using var host = Host(planner, services => services.AddScoped<IAnalyticsService>(provider => new AnalyticsService(
            provider.GetRequiredService<IAnalyticsRepository>(),
            planner,
            clock,
            new AnalyticsSettings(TimeZoneInfo.Utc),
            provider.GetRequiredService<ILogger<AnalyticsService>>())));
        using var anna = host.CreateClientFor(AuthTestHost.Token());

        var first = await AskAsync(anna, new { question = "Сколько потратили в этом месяце?" });
        Assert.Equal("2026-09-01", first.GetProperty("plan").GetProperty("from").GetString());
        Assert.Equal("2026-09-15", first.GetProperty("plan").GetProperty("to").GetString());
        var id = first.GetProperty("historyId").GetString();

        clock.Now = new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.Zero);
        var repeated = await AskAsync(anna, new { question = "Сколько потратили в этом месяце?" });
        Assert.True(repeated.GetProperty("reused").GetBoolean());
        Assert.Equal("2026-10-01", repeated.GetProperty("plan").GetProperty("from").GetString());
        Assert.Equal("2026-10-03", repeated.GetProperty("plan").GetProperty("to").GetString());

        clock.Now = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);
        var refreshed = await (await anna.PostAsync($"/api/analytics/history/{id}/refresh?locale=ru", null, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("2026-11-01", refreshed.GetProperty("answer").GetProperty("plan").GetProperty("from").GetString());
        Assert.Equal(1, planner.Calls);
    }

    [Fact]
    public async Task OnlyTheNewestEntriesAreKept()
    {
        await using var application = await TestApplication.CreateAsync();
        var start = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAnalyticsHistoryRepository>();
            for (var index = 0; index < 5; index++)
            {
                repository.Add(AnalyticsQuestionEntry.Create(
                    Guid.NewGuid(), $"Вопрос {index}", false, "{}", null, "{}", "", "user-anna", null, start.AddMinutes(index)));
            }

            await repository.SaveChangesAsync(Cancel);
            await repository.TrimAsync(3, Cancel);
        }

        await using (var scope = application.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAnalyticsHistoryRepository>();
            var (rows, hasMore) = await repository.ListAsync(0, 10, Cancel);
            Assert.Equal(["Вопрос 4", "Вопрос 3", "Вопрос 2"], rows.Select(row => row.Question));
            Assert.False(hasMore);
        }
    }

    [Fact]
    public async Task AnExistingDatabaseGainsAnEmptyHistory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "purchase-memory-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<PurchaseMemoryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "migration.db")};Pooling=False")
                .Options;
            await using (var oldContext = new PurchaseMemoryDbContext(options))
            {
                await oldContext.Database.MigrateAsync("20261003172136_AddProductTags", Cancel);
            }

            await using var upgraded = new PurchaseMemoryDbContext(options);
            await upgraded.Database.MigrateAsync(Cancel);
            Assert.Empty(await upgraded.AnalyticsQuestions.ToListAsync(Cancel));
            upgraded.AnalyticsQuestions.Add(AnalyticsQuestionEntry.Create(
                Guid.NewGuid(), "Сколько?", false, "{}", null, "{}", "Ответ", "user-anna", "anna@example.com", DateTimeOffset.UtcNow));
            await upgraded.SaveChangesAsync(Cancel);
            Assert.Equal("сколько?", (await upgraded.AnalyticsQuestions.SingleAsync(Cancel)).NormalizedQuestion);
            Assert.False(upgraded.Database.HasPendingModelChanges());

            // The migration rolls back cleanly as well.
            await upgraded.Database.MigrateAsync("20261003172136_AddProductTags", Cancel);
            Assert.DoesNotContain("20261008150642_AddAnalyticsQuestions", await upgraded.Database.GetAppliedMigrationsAsync(Cancel));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static AuthTestHost Host(FakePlanner planner, Action<IServiceCollection>? configure = null) =>
        new(configureServices: services =>
        {
            services.AddSingleton<IAnalyticsQueryPlanner>(planner);
            configure?.Invoke(services);
        });

    private static async Task<JsonElement> AskAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/analytics/ask", body, Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
    }

    private sealed class FailingSaves
    {
        public bool Enabled { get; set; }
    }

    private sealed class FailingHistoryRepository(IAnalyticsHistoryRepository inner, FailingSaves failing) : IAnalyticsHistoryRepository
    {
        public Task<AnalyticsQuestionEntry?> FindAsync(Guid id, CancellationToken cancellationToken) => inner.FindAsync(id, cancellationToken);

        public Task<AnalyticsQuestionEntry?> FindLatestAsync(string normalizedQuestion, bool isFollowUp, string? planJson, CancellationToken cancellationToken) =>
            inner.FindLatestAsync(normalizedQuestion, isFollowUp, planJson, cancellationToken);

        public Task<(IReadOnlyList<AnalyticsHistoryRow> Rows, bool HasMore)> ListAsync(int offset, int limit, CancellationToken cancellationToken) =>
            inner.ListAsync(offset, limit, cancellationToken);

        public void Add(AnalyticsQuestionEntry entry) => inner.Add(entry);

        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) => inner.DeleteAsync(id, cancellationToken);

        public Task TrimAsync(int keep, CancellationToken cancellationToken) => inner.TrimAsync(keep, cancellationToken);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => failing.Enabled
            ? throw new InvalidOperationException("The history store is unavailable.")
            : inner.SaveChangesAsync(cancellationToken);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakePlanner : IAnalyticsQueryPlanner
    {
        private int _calls;

        public bool Configured { get; set; } = true;

        public bool IsConfigured => Configured;

        public string ModelName => "fake";

        public int Calls => _calls;

        public AnalyticsGroupBy GroupBy { get; set; } = AnalyticsGroupBy.Month;

        public AnalyticsPeriodRequest? Period { get; set; }

        public Task<AnalyticsPlanningResult> PlanAsync(AnalyticsPlanningRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            var period = Period is null
                ? new ResolvedPeriod(new DateOnly(request.Today.Year, 1, 1), null, true)
                : AnalyticsPeriodResolver.Resolve(Period, request.Today);
            return Task.FromResult(new AnalyticsPlanningResult(
                new AnalyticsPlan { Metric = AnalyticsMetric.Spend, From = period.From, To = period.To, GroupBy = GroupBy },
                true,
                [],
                10,
                5,
                Period));
        }
    }
}

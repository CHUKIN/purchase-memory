using PurchaseMemory.Application.Products.PhotoNames;
using PurchaseMemory.Application.Products.PhotoSearch;
using PurchaseMemory.FindByPhotoBenchmark;

namespace PurchaseMemory.IntegrationTests;

/// <summary>The «Найти по фото» benchmark without Claude: a fake matcher, no network, no money.</summary>
public sealed class FindByPhotoBenchmarkCoreTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static readonly Guid Kefir = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Tomatoes = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Bread = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    [Theory]
    [InlineData("photos labels.csv cands.json out.csv")]
    [InlineData("photos labels.csv cands.json out.csv --budget-usd 0")]
    [InlineData("photos labels.csv cands.json out.csv --budget-usd -1")]
    [InlineData("photos labels.csv cands.json out.csv --budget-usd lots")]
    [InlineData("photos labels.csv cands.json out.csv --budget-usd")]
    [InlineData("photos labels.csv out.csv --budget-usd 0.1")]
    [InlineData("photos labels.csv cands.json out.csv --budget-usd 0.1 --api-key x")]
    [InlineData("photos labels.csv cands.json out.csv --budget-usd 0.1 --max-candidates 5")]
    public void RefusesToRunWithoutAPositiveBudgetOrWithBadArguments(string commandLine) =>
        Assert.Throws<ArgumentException>(() => BenchmarkArguments.Parse(commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    [Fact]
    public void ParsesArgumentsLabelsAndBothCandidateShapes()
    {
        var arguments = BenchmarkArguments.Parse(["photos", "labels.csv", "cands", "out.csv", "--budget-usd", "0.10", "--limit", "30", "--model", "claude-haiku-5-5"]);
        Assert.Equal((0.10m, 30, 300, "claude-haiku-5-5"), (arguments.BudgetUsd, arguments.Limit, arguments.MaxCandidates, arguments.Model));

        var labels = BenchmarkInputs.ParseLabels("﻿file,expected\nkefir.jpg,Kefir naturalny Activia 280 g\n\"tomato, red.jpg\",POMIDOR MALINOWY LUZ; aaaaaaaa-0000-0000-0000-000000000003\nunknown.jpg,none\nempty.jpg,\n");
        Assert.Equal(["kefir.jpg", "tomato, red.jpg", "unknown.jpg"], labels.Select(label => label.File));
        Assert.True(labels[2].ExpectsNone);

        var candidates = BenchmarkInputs.ParseCandidates(
        [
            $$"""[{"productId":"{{Kefir}}","receiptName":"ACTIV KEFI NAT 280","namePl":"Kefir naturalny Activia 280 g","nameRu":"Кефир Activia","brand":"Activia","category":"Nabiał"}]""",
            $$"""{"items":[{"id":"{{Tomatoes}}","name":"POMIDOR MALINOWY LUZ","namePl":null},{"id":"{{Bread}}","name":"CHLEB ZYT"},{"id":"{{Kefir}}","name":"dup"}],"totalCount":3}"""
        ]);
        Assert.Equal([Kefir, Tomatoes, Bread], candidates.Select(candidate => candidate.ProductId));
        Assert.Equal("Nabiał", candidates[0].Category);

        Assert.Equal([Kefir], BenchmarkInputs.Resolve(labels[0], candidates)!);
        Assert.Equal(new HashSet<Guid> { Tomatoes, Bread }, BenchmarkInputs.Resolve(labels[1], candidates)!.ToHashSet());
        Assert.Empty(BenchmarkInputs.Resolve(labels[2], candidates)!);
        Assert.Null(BenchmarkInputs.Resolve(new PhotoLabel("x.jpg", ["Mleko"]), candidates));
    }

    [Fact]
    public async Task ScoresTopOneTopThreeAndNotInListAndSumsTheCost()
    {
        var matcher = new ScriptedMatcher(
            [Kefir, Tomatoes],      // kefir.jpg: first is right
            [Bread, Tomatoes],      // tomato.jpg: right one second
            [Kefir],                // bread.jpg: wrong
            [],                     // none.jpg: correctly empty
            null);                  // fail.jpg: the model failed
        var photos = new[]
        {
            Photo("kefir.jpg", Kefir), Photo("tomato.jpg", Tomatoes), Photo("bread.jpg", Bread),
            new BenchmarkPhoto(new PhotoLabel("none.jpg", ["none"]), new HashSet<Guid>(), Image),
            Photo("fail.jpg", Kefir),
            new BenchmarkPhoto(new PhotoLabel("broken.heic", [Kefir.ToString()]), new HashSet<Guid> { Kefir }, () => null)
        };

        var report = await new FindByPhotoBenchmarkRunner(matcher, 1m, HaikuWorstCase).RunAsync(photos, [], Cancel);

        var summary = report.Summary;
        Assert.Equal((6, 3, 1, 2), (summary.Photos, summary.InListPhotos, summary.NonePhotos, summary.Failed));
        Assert.Equal(0.333m, summary.Top1Rate);
        Assert.Equal(0.667m, summary.Top3Rate);
        Assert.Equal(1m, summary.NoneCorrectRate);
        // Four answered calls at 0.002 plus the failed one charged at the worst case (0.004); the undecodable photo is free.
        Assert.Equal(0.012m, summary.TotalCostUsd);
        Assert.Equal(0.0024m, summary.MeanCostUsd);
        Assert.False(summary.StoppedByBudget);
        Assert.Equal(5, matcher.Calls);
        Assert.Contains("top-1 33.3%; top-3 66.7%", BenchmarkOutput.SummaryText(summary), StringComparison.Ordinal);
        Assert.Equal(7, BenchmarkOutput.CsvRows(report).Count);
    }

    [Fact]
    public async Task StopsBeforeACallCouldCrossTheBudget()
    {
        var matcher = new ScriptedMatcher([Kefir], [Kefir], [Kefir], [Kefir], [Kefir]) { CostPerCall = 0.004m };
        var photos = Enumerable.Range(0, 5).Select(index => Photo($"{index}.jpg", Kefir)).ToList();

        // Every call is assumed at the worst case 0.006: 0.004 + 0.004 = 0.008, then 0.008 + 0.006 > 0.010 stops.
        var report = await new FindByPhotoBenchmarkRunner(matcher, 0.010m, 0.006m).RunAsync(photos, [], Cancel);

        Assert.Equal(2, matcher.Calls);
        Assert.True(report.Summary.StoppedByBudget);
        Assert.Equal(0.008m, report.Summary.TotalCostUsd);

        // A budget below the worst case of one call spends nothing at all.
        var none = new ScriptedMatcher([Kefir]);
        Assert.True((await new FindByPhotoBenchmarkRunner(none, 0.003m, HaikuWorstCase).RunAsync(photos, [], Cancel)).Summary.StoppedByBudget);
        Assert.Equal(0, none.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FindByPhotoBenchmarkRunner(none, 0m, HaikuWorstCase).RunAsync(photos, [], Cancel));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FindByPhotoBenchmarkRunner(none, 1m, 0m).RunAsync(photos, [], Cancel));
    }

    [Fact]
    public async Task AfterACheapAnswerFailedCallsAreStillChargedAtTheWorstCaseAndTheBudgetHolds()
    {
        // Haiku: 20 000 input tokens at $0.10/M + 4 000 output tokens at $0.50/M.
        Assert.Equal(0.004m, HaikuWorstCase);
        var matcher = new ScriptedMatcher([Kefir], null, null, null, null) { CostPerCall = 0.001m };
        var photos = Enumerable.Range(0, 5).Select(index => Photo($"{index}.jpg", Kefir)).ToList();

        var report = await new FindByPhotoBenchmarkRunner(matcher, 0.010m, HaikuWorstCase).RunAsync(photos, [], Cancel);

        // 0.001, then two failures at 0.004 each = 0.009; a third failure could reach 0.013 > 0.010.
        Assert.Equal(3, matcher.Calls);
        Assert.Equal(0.009m, report.Summary.TotalCostUsd);
        Assert.True(report.Summary.TotalCostUsd <= 0.010m);
        Assert.True(report.Summary.StoppedByBudget);
    }

    private static decimal HaikuWorstCase => FindByPhotoBenchmarkRunner.WorstCaseCallUsd(0.10m, 0.50m, 4_000);

    [Fact]
    public async Task UsageWithoutAPriceIsChargedAtTheCeilingSoTheBudgetStillHolds()
    {
        // 13 000 input + 100 output tokens at $30 / $150 per million = $0.405 per call.
        var reading = new ProductPhotoMatchReading([], 0, 13_000, 100, null);
        Assert.Equal(0.405m, FindByPhotoBenchmarkRunner.Charge(reading, 0.005m));
        Assert.Equal(0.005m, FindByPhotoBenchmarkRunner.Charge(reading with { InputTokens = null, OutputTokens = null }, 0.005m));
        Assert.Equal(0.001m, FindByPhotoBenchmarkRunner.Charge(reading with { CostUsd = 0.001m }, 0.005m));

        var matcher = new ScriptedMatcher([Kefir], [Kefir], [Kefir]) { CostPerCall = null };
        var report = await new FindByPhotoBenchmarkRunner(matcher, 0.5m, HaikuWorstCase)
            .RunAsync(Enumerable.Range(0, 3).Select(index => Photo($"{index}.jpg", Kefir)).ToList(), [], Cancel);

        // The first call is charged $0.405; a second could reach $0.81 > $0.50.
        Assert.Equal(1, matcher.Calls);
        Assert.True(report.Summary.StoppedByBudget);
        Assert.Equal(0.405m, report.Summary.TotalCostUsd);
    }

    private static ProductPhotoImage? Image() => new(ProductPhotoNameTests.Photo(), "image/jpeg");

    private static BenchmarkPhoto Photo(string file, Guid expected) =>
        new(new PhotoLabel(file, [expected.ToString()]), new HashSet<Guid> { expected }, Image);

    /// <summary>Answers each call with the next script line; a null line fails.</summary>
    private sealed class ScriptedMatcher(params Guid[]?[] script) : IProductPhotoMatcher
    {
        public int Calls { get; private set; }

        public decimal? CostPerCall { get; init; } = 0.002m;

        public bool IsEnabled => true;

        public bool IsConfigured => true;

        public string ModelName => "fake";

        public int MaximumCandidates => 300;

        public Task<ProductPhotoMatchReading> MatchAsync(ProductPhotoImage image, IReadOnlyList<ProductPhotoCandidate> candidates, CancellationToken cancellationToken)
        {
            var line = script[Calls++];
            return line is null
                ? Task.FromException<ProductPhotoMatchReading>(new ProductPhotoSearchUnavailableException("fake failure"))
                : Task.FromResult(new ProductPhotoMatchReading(
                    line.Select(id => new ProductPhotoMatch(id, ProductPhotoMatchConfidence.Medium)).ToList(), 0, 13_000, 100, CostPerCall));
        }
    }
}

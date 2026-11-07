using PurchaseMemory.Application.Purchases.HaulPhoto;
using PurchaseMemory.HaulPhotoBenchmark;

namespace PurchaseMemory.IntegrationTests;

/// <summary>The «Сфотографировать покупки» benchmark's core with a fake matcher: no network, no money.</summary>
public sealed class HaulPhotoBenchmarkCoreTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("photos hauls.json out.csv")]
    [InlineData("photos hauls.json out.csv --budget-usd 0")]
    [InlineData("photos hauls.json out.csv --budget-usd -1")]
    [InlineData("photos hauls.json out.csv --budget-usd lots")]
    [InlineData("photos hauls.json --budget-usd 0.5")]
    [InlineData("photos hauls.json out.csv --budget-usd 0.5 --effort max")]
    [InlineData("photos hauls.json out.csv --budget-usd 0.5 --limit 0")]
    [InlineData("photos hauls.json out.csv --budget-usd 0.5 --free")]
    [InlineData("photos hauls.json out.csv --budget-usd")]
    public void ArgumentsWithoutAPositiveBudgetOrWithBadValuesAreRefused(string args) =>
        Assert.Throws<ArgumentException>(() => BenchmarkArguments.Parse(args.Split(' ')));

    [Fact]
    public void ArgumentsAreRead()
    {
        var parsed = BenchmarkArguments.Parse(
            ["photos", "hauls.json", "out.csv", "--budget-usd", "0.50", "--model", "claude-haiku-5-5", "--effort", "Low", "--limit", "5", "--crops", "crops"]);
        Assert.Equal(new BenchmarkArguments("photos", "hauls.json", "out.csv", 0.50m, "claude-haiku-5-5", "low", 5, "crops"), parsed);
    }

    [Fact]
    public void LabelsTakeInlineLinesOrASavedPurchaseAndResolveExpectedNames()
    {
        const string labels = """
            [
              {"file": "a.jpg", "lines": [{"name": "MLEKO UHT", "product": "Mleko UHT 3,2%", "quantity": 1, "unit": "szt"}, {"name": "JOGURT NAT", "quantity": 2}],
               "expected": ["mleko uht 3,2%", "JOGURT NAT", "JOGURT NAT"]},
              {"file": "b.jpg", "purchase": "purchase-b.json", "expected": ["CHLEB"]},
              {"file": "c.jpg", "lines": [{"name": "SER"}], "expected": ["MASLO"]}
            ]
            """;
        const string purchase = """
            {"id": "x", "items": [
              {"name": "CHLEB", "productNamePl": "Chleb żytni", "quantity": 1, "unit": "szt", "kind": "product"},
              {"name": "RABAT", "quantity": null, "kind": "discount"}
            ]}
            """;

        var parsed = BenchmarkInputs.ParseLabels(labels, name => name == "purchase-b.json" ? purchase : throw new FileNotFoundException(name));

        Assert.Equal(3, parsed.Count);
        var a = parsed[0];
        Assert.Equal(("Mleko UHT 3,2%", 1m, "szt"), (a.Lines[0].ProductName, a.Lines[0].Quantity, a.Lines[0].Unit));
        Assert.Equal([a.Lines[0].LineId, a.Lines[1].LineId, a.Lines[1].LineId], BenchmarkInputs.Resolve(a));
        var b = Assert.Single(parsed[1].Lines);
        Assert.Equal(("CHLEB", "Chleb żytni"), (b.ReceiptName, b.ProductName));
        // A name that is no line: the photo is skipped rather than scored wrong.
        Assert.Null(BenchmarkInputs.Resolve(parsed[2]));

        Assert.Throws<System.Text.Json.JsonException>(() => BenchmarkInputs.ParseLabels("""[{"file": "d.jpg", "expected": []}]""", _ => ""));
        Assert.Throws<System.Text.Json.JsonException>(() => BenchmarkInputs.ParseLabels("""{"file": "d.jpg"}""", _ => ""));
    }

    [Fact]
    public void PairsAreScoredAsAMultiset()
    {
        var milk = Guid.NewGuid();
        var yogurt = Guid.NewGuid();
        var bread = Guid.NewGuid();
        var box = HaulBox.TryFromSize(0.1, 0.1, 0.2, 0.2)!;
        var score = HaulPhotoBenchmarkRunner.Score(
            [milk, yogurt, yogurt, bread],
            [
                new HaulPhotoDetection(box, milk, HaulMatchConfidence.High),
                new HaulPhotoDetection(box, yogurt, HaulMatchConfidence.High),
                new HaulPhotoDetection(box, milk, HaulMatchConfidence.Low),
                new HaulPhotoDetection(box, Guid.NewGuid(), HaulMatchConfidence.Low),
                new HaulPhotoDetection(box, null, HaulMatchConfidence.Low)
            ]);
        // milk ✓, yogurt ✓ (one of two), a second milk ✗, a stranger ✗; one yogurt and the bread missed.
        Assert.Equal((2, 2, 2, 1), score);
    }

    [Fact]
    public async Task TheRunStopsBeforeTheBudgetCouldBeCrossedAndCountsEverything()
    {
        var lines = new List<HaulPhotoLine> { new(Guid.NewGuid(), "MLEKO", null, 1m, null) };
        var label = new HaulLabel("a.jpg", lines, ["MLEKO"]);
        var image = new HaulPhotoImage(new byte[] { 1 }, "image/jpeg", 100, 100);
        var photos = Enumerable.Range(0, 10)
            .Select(index => new BenchmarkPhoto(label with { File = $"{index}.jpg" }, [lines[0].LineId], () => image))
            .Prepend(new BenchmarkPhoto(label with { File = "broken.jpg" }, [lines[0].LineId], () => null))
            .ToList();
        var matcher = new CostMatcher(lines[0].LineId, 0.04m);

        var report = await new HaulPhotoBenchmarkRunner(matcher, 0.15m, SonnetWorstCase).RunAsync(photos, Cancel);

        // Every call is assumed at the worst case $0.092: 0.04 + 0.04 = 0.08, then 0.08 + 0.092 > 0.15 stops.
        Assert.Equal(2, matcher.Calls);
        Assert.True(report.Summary.StoppedByBudget);
        Assert.Equal(0.08m, report.Summary.TotalCostUsd);
        Assert.Equal(3, report.Summary.Photos);
        Assert.Equal(1, report.Summary.Failed);
        Assert.Equal((2, 0, 0), (report.Summary.CorrectPairs, report.Summary.WrongPairs, report.Summary.MissedItems));
        Assert.Equal(1m, report.Summary.Precision);
        Assert.Equal(1m, report.Summary.Recall);
        Assert.Equal(0.04m, report.Summary.MeanCostUsd);
        Assert.Contains("STOPPED by the budget", BenchmarkOutput.SummaryText(report.Summary), StringComparison.Ordinal);
        Assert.Equal(4, BenchmarkOutput.CsvRows(report).Count);

        // A budget below the worst case of one call sends nothing at all.
        var none = new CostMatcher(lines[0].LineId, 0.01m);
        var empty = await new HaulPhotoBenchmarkRunner(none, 0.09m, SonnetWorstCase).RunAsync(photos.Skip(1).ToList(), Cancel);
        Assert.Equal(0, none.Calls);
        Assert.True(empty.Summary.StoppedByBudget);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new HaulPhotoBenchmarkRunner(none, 1m, 0m).RunAsync(photos, Cancel));
    }

    [Fact]
    public async Task AfterACheapAnswerFailedCallsAreStillChargedAtTheWorstCaseAndTheBudgetHolds()
    {
        // Sonnet 5.5: 6 000 input tokens at $2/M + 8 000 output tokens at $10/M.
        Assert.Equal(0.092m, SonnetWorstCase);
        var lines = new List<HaulPhotoLine> { new(Guid.NewGuid(), "MLEKO", null, 1m, null) };
        var label = new HaulLabel("a.jpg", lines, ["MLEKO"]);
        var image = new HaulPhotoImage(new byte[] { 1 }, "image/jpeg", 100, 100);
        var matcher = new CheapThenFailingMatcher(lines[0].LineId);

        var report = await new HaulPhotoBenchmarkRunner(matcher, 0.20m, SonnetWorstCase)
            .RunAsync(Enumerable.Range(0, 5).Select(_ => new BenchmarkPhoto(label, [lines[0].LineId], () => image)).ToList(), Cancel);

        // $0.01, then two failures at $0.092 each = $0.194; a third failure could reach $0.286 > $0.20.
        Assert.Equal(3, matcher.Calls);
        Assert.Equal(0.194m, report.Summary.TotalCostUsd);
        Assert.True(report.Summary.TotalCostUsd <= 0.20m);
        Assert.True(report.Summary.StoppedByBudget);
    }

    private static decimal SonnetWorstCase => HaulPhotoBenchmarkRunner.WorstCaseCallUsd(2m, 10m, 8_000);

    [Fact]
    public async Task FailuresAndUnpricedAnswersAreChargedOnTheSafeSide()
    {
        var withoutCost = new HaulPhotoReading([], 0, 4_000, 2_000, null);
        // 4k × $30/M + 2k × $150/M = $0.42 at the ceiling price.
        Assert.Equal(0.42m, HaulPhotoBenchmarkRunner.Charge(withoutCost, 0.10m));
        Assert.Equal(0.10m, HaulPhotoBenchmarkRunner.Charge(new HaulPhotoReading([], 0, null, null, null), 0.10m));
        Assert.Equal(0.03m, HaulPhotoBenchmarkRunner.Charge(new HaulPhotoReading([], 0, 1, 1, 0.03m), 0.10m));

        var lines = new List<HaulPhotoLine> { new(Guid.NewGuid(), "MLEKO", null, 1m, null) };
        var label = new HaulLabel("a.jpg", lines, ["MLEKO"]);
        var image = new HaulPhotoImage(new byte[] { 1 }, "image/jpeg", 100, 100);
        var failing = new FailingMatcher();
        var report = await new HaulPhotoBenchmarkRunner(failing, 0.25m, SonnetWorstCase)
            .RunAsync(Enumerable.Range(0, 5).Select(_ => new BenchmarkPhoto(label, [lines[0].LineId], () => image)).ToList(), Cancel);
        // Each failure is charged at the $0.092 worst case: two fit into $0.25, a third could not.
        Assert.Equal(2, failing.Calls);
        Assert.Equal(0.184m, report.Summary.TotalCostUsd);
        Assert.Equal(2, report.Summary.Failed);
        Assert.Null(report.Summary.Precision);
    }

    private sealed class CostMatcher(Guid lineId, decimal cost) : IHaulPhotoMatcher
    {
        public int Calls { get; private set; }

        public bool IsEnabled => true;

        public bool IsConfigured => true;

        public string ModelName => "fake";

        public Task<HaulPhotoReading> MatchAsync(HaulPhotoImage image, IReadOnlyList<HaulPhotoLine> lines, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HaulPhotoReading(
                [new HaulPhotoDetection(HaulBox.TryFromSize(0.1, 0.1, 0.2, 0.2)!, lineId, HaulMatchConfidence.High)],
                0, 3_000, 2_000, cost));
        }
    }

    private sealed class CheapThenFailingMatcher(Guid lineId) : IHaulPhotoMatcher
    {
        public int Calls { get; private set; }

        public bool IsEnabled => true;

        public bool IsConfigured => true;

        public string ModelName => "fake";

        public Task<HaulPhotoReading> MatchAsync(HaulPhotoImage image, IReadOnlyList<HaulPhotoLine> lines, CancellationToken cancellationToken)
        {
            Calls++;
            return Calls == 1
                ? Task.FromResult(new HaulPhotoReading(
                    [new HaulPhotoDetection(HaulBox.TryFromSize(0.1, 0.1, 0.2, 0.2)!, lineId, HaulMatchConfidence.High)], 0, 3_000, 500, 0.01m))
                : Task.FromException<HaulPhotoReading>(new HaulPhotoUnavailableException("Claude did not finish (stop_reason:max_tokens)."));
        }
    }

    private sealed class FailingMatcher : IHaulPhotoMatcher
    {
        public int Calls { get; private set; }

        public bool IsEnabled => true;

        public bool IsConfigured => true;

        public string ModelName => "fake";

        public Task<HaulPhotoReading> MatchAsync(HaulPhotoImage image, IReadOnlyList<HaulPhotoLine> lines, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromException<HaulPhotoReading>(new HaulPhotoUnavailableException("Claude did not answer (timeout)."));
        }
    }
}

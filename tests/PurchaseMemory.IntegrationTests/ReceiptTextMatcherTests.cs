using System.Text.Json;
using PurchaseMemory.Application.Receipts.TextLocation;
using PurchaseMemory.Domain.Receipts;
using PurchaseMemory.Infrastructure.TextLocation;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// The C# port of the stage 1.3 study, checked offline on real cached Azure
/// responses (test-data household-2026-08, the OCR line layer prebuilt-read
/// returns) and the saved Haiku run the study used. The score is the study's:
/// a ground-truth line is "right" when the text under its highlight shows its
/// name, or its price without another line's name.
/// </summary>
public sealed class ReceiptTextMatcherTests(ITestOutputHelper output)
{
    private static readonly string Dataset = Path.Combine(
        RepositoryRoot(), "test-data", "receipts", "household-2026-08", "results");

    [Fact]
    public void MatchesEveryGroundTruthLineOfTheHouseholdSet()
    {
        using var pairsDocument = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestData", "highlight-h08-pairs.json")));
        var verdicts = new Dictionary<string, int>(StringComparer.Ordinal);
        var failures = new List<string>();
        foreach (var receipt in pairsDocument.RootElement.GetProperty("receipts").EnumerateObject())
        {
            var layout = LoadLayout(receipt.Name);
            var haiku = LoadHaikuItems(receipt.Name);
            var groundTruth = receipt.Value.GetProperty("groundTruth").EnumerateArray()
                .Select(item => (Name: item.GetProperty("name").GetString()!, LineTotal: item.GetProperty("lineTotal").GetDecimal()))
                .ToList();
            var located = ReceiptTextMatcher.Locate(layout, haiku);
            var words = layout.Lines.SelectMany(line => line.Words).ToList();
            foreach (var pair in receipt.Value.GetProperty("pairs").EnumerateArray())
            {
                var k = pair.GetProperty("groundTruthIndex").GetInt32();
                var haikuIndex = pair.GetProperty("haikuIndex").GetInt32();
                var verdict = located.TryGetValue(haikuIndex, out var polygons)
                    ? Verdict(groundTruth, k, TextUnder(polygons, words, layout))
                    : "no-shape";
                verdicts[verdict] = verdicts.GetValueOrDefault(verdict) + 1;
                if (verdict != "right")
                {
                    failures.Add($"{receipt.Name}: {groundTruth[k].Name} -> {verdict}");
                }
            }
        }

        output.WriteLine("Verdicts: " + string.Join(", ", verdicts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")));
        foreach (var failure in failures)
        {
            output.WriteLine(failure);
        }

        Assert.Equal(110, verdicts.Values.Sum());
        Assert.Equal(110, verdicts.GetValueOrDefault("right"));
    }

    /// <summary>
    /// Same OCR lines per item and the same outlines (within rounding) as the
    /// study's Python code on all 20 receipts — the fixture was produced by
    /// <c>analyze.py</c> itself (<c>hybrid_words</c> + <c>snake</c> with the combo paddings).
    /// </summary>
    [Fact]
    public void ReproducesThePythonReferenceLineForLine()
    {
        using var reference = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestData", "highlight-h08-python-reference.json")));
        var compared = 0;
        foreach (var receipt in reference.RootElement.GetProperty("receipts").EnumerateObject())
        {
            var layout = LoadLayout(receipt.Name);
            var haiku = LoadHaikuItems(receipt.Name);
            var matches = ReceiptTextMatcher.FindWords(layout.Lines, haiku).ToDictionary(match => match.ItemIndex);
            var located = ReceiptTextMatcher.Locate(layout, haiku);
            var expectedItems = receipt.Value.EnumerateArray().ToList();
            Assert.Equal(haiku.Count, expectedItems.Count);
            for (var index = 0; index < expectedItems.Count; index++)
            {
                var expected = expectedItems[index];
                if (expected.ValueKind == JsonValueKind.Null)
                {
                    Assert.False(matches.ContainsKey(index), $"{receipt.Name}#{index} should not match");
                    continue;
                }

                var expectedLines = expected.GetProperty("lines").EnumerateArray().Select(line => line.GetInt32()).ToList();
                var actualLines = matches[index].WordGroups
                    .Select(words => FindLineIndex(layout, words[0]))
                    .Order()
                    .ToList();
                Assert.Equal(expectedLines, actualLines);

                var expectedPolygons = expected.GetProperty("polygons").EnumerateArray()
                    .Select(polygon => polygon.EnumerateArray().Select(value => value.GetDouble()).ToList())
                    .ToList();
                var actualPolygons = located[index];
                Assert.Equal(expectedPolygons.Count, actualPolygons.Count);
                for (var shape = 0; shape < expectedPolygons.Count; shape++)
                {
                    var want = SortedPoints(expectedPolygons[shape]);
                    var got = SortedPoints(actualPolygons[shape].Points);
                    Assert.Equal(want.Count, got.Count);
                    for (var point = 0; point < want.Count; point++)
                    {
                        Assert.Equal(want[point].X, got[point].X, 0.0003);
                        Assert.Equal(want[point].Y, got[point].Y, 0.0003);
                    }
                }

                compared++;
            }
        }

        output.WriteLine($"Items identical to the Python reference: {compared}");
        Assert.Equal(194, compared);
    }

    private static int FindLineIndex(ReceiptTextLayout layout, ReceiptTextWord word) =>
        layout.Lines.Select((line, index) => (line, index)).First(entry => entry.line.Words.Contains(word)).index;

    private static List<(double X, double Y)> SortedPoints(IReadOnlyList<double> flat) =>
        Enumerable.Range(0, flat.Count / 2)
            .Select(index => (X: Math.Round(flat[index * 2], 3), Y: Math.Round(flat[index * 2 + 1], 3), RawX: flat[index * 2], RawY: flat[index * 2 + 1]))
            .OrderBy(point => point.X).ThenBy(point => point.Y)
            .Select(point => (point.RawX, point.RawY))
            .ToList();

    [Fact]
    public void EveryCachedReceiptGivesOutlinesInsideThePhoto()
    {
        var located = 0;
        var items = 0;
        for (var number = 1; number <= 20; number++)
        {
            var id = number.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            var layout = LoadLayout(id);
            var haiku = LoadHaikuItems(id);
            var found = ReceiptTextMatcher.Locate(layout, haiku);
            items += haiku.Count;
            located += found.Count;
            foreach (var polygon in found.Values.SelectMany(polygons => polygons))
            {
                Assert.InRange(polygon.PointCount, 3, 8);
                Assert.All(polygon.Points, value => Assert.InRange(value, 0, 1));
            }
        }

        output.WriteLine($"Haiku lines with Azure outlines: {located}/{items}");
        Assert.True(located >= items * 0.9, $"only {located} of {items} lines located");
    }

    [Theory]
    [InlineData("abcd", "bcde", 0.75)]
    [InlineData("mleko 3,2%", "mleko 2%", 0.8888888888888888)]
    [InlineData("Ogórki grunt. Pl uz F", "Ogorki grunt.Pl uz F 1,60 A", 0.7916666666666666)]
    [InlineData("kefir naturalny", "kefir", 0.5)]
    public void RatioEqualsPythonDifflib(string a, string b, double expected) =>
        Assert.Equal(expected, ReceiptTextSimilarity.Ratio(a, b), 12);

    [Fact]
    public void RatioAppliesDifflibAutojunkOnLongSequences() =>
        Assert.Equal(
            0.004081632653061225,
            ReceiptTextSimilarity.Ratio(new string('a', 150) + new string('b', 100), string.Concat(Enumerable.Repeat("ab", 120))),
            12);

    [Theory]
    [InlineData("C_Fairy Kapsułki Łódź 1234567", "fairy kapsułki łodz")]
    [InlineData("A#Mleko 3,2% UHT", "mleko 3 2% uht")]
    [InlineData("Żółć  — test", "zołc test")]
    public void NormalizeEqualsTheScoringScript(string text, string expected) =>
        Assert.Equal(expected, ReceiptTextSimilarity.Normalize(text));

    [Theory]
    [InlineData("Ogórki grunt. Pl uz F", "Ogorki grunt.Pl uz F 1,60 A", 1.0)]
    [InlineData("FAIRY KAPS", "C_Fairy Kapsulki Lemon 84,99 C", 1.0)]
    [InlineData("xy", "xy z", 0.0)]
    public void PartialSimilarityFindsANameInsideALongerLine(string name, string text, double expected) =>
        Assert.Equal(expected, ReceiptTextSimilarity.PartialSimilarity(name, text), 12);

    [Theory]
    [InlineData(5.98, "Chleb 5,98 C", true)]
    [InlineData(5.98, "Chleb 5. 98", true)]
    [InlineData(-0.2, "OPUST -0,20", true)]
    [InlineData(5.98, "Chleb 15,98", false)]
    [InlineData(5.98, "Chleb 5,99", false)]
    public void PricePrintedAcceptsPolishAndDottedAmounts(double amount, string text, bool expected) =>
        Assert.Equal(expected, ReceiptTextSimilarity.PricePrinted((decimal)amount, text));

    [Fact]
    public void AnItemFoundNowhereKeepsNoOutline()
    {
        var layout = LoadLayout("04");
        var found = ReceiptTextMatcher.Locate(layout, [new ReceiptTextMatchItem("Zupełnie inny produkt", 999.99m)]);
        Assert.Empty(found);
    }

    [Fact]
    public void APrintedLineIsUsedForOneItemOnly()
    {
        var layout = LoadLayout("04");
        var haiku = LoadHaikuItems("04");
        var twice = new List<ReceiptTextMatchItem> { haiku[0], haiku[0] };
        var matches = ReceiptTextMatcher.FindWords(layout.Lines, twice);
        var first = matches.Single(match => match.ItemIndex == 0).WordGroups.SelectMany(words => words).ToHashSet();
        var second = matches.SingleOrDefault(match => match.ItemIndex == 1)?.WordGroups.SelectMany(words => words) ?? [];
        Assert.DoesNotContain(second, first.Contains);
    }

    internal static ReceiptTextLayout LoadLayout(string receiptId)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(Dataset, "ocr-compare", "cache", "azure", $"receipt_{receiptId}.json")));
        var page = AzureDocumentIntelligenceTextLocator.ParsePage(document.RootElement.GetProperty("response"), "test")
            ?? throw new InvalidOperationException($"No page in receipt {receiptId}.");
        return ReceiptTextLayout.Create(Guid.NewGuid(), page.Source, page.Width, page.Height, page.Lines, DateTimeOffset.UnixEpoch);
    }

    internal static List<ReceiptTextMatchItem> LoadHaikuItems(string receiptId)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Dataset, "ocr-compare", "runs", "haiku-prod-v4-medium", $"haiku-prod-v4-medium__claude-haiku-5-5__{receiptId}.json")));
        return document.RootElement.GetProperty("parsed").GetProperty("items").EnumerateArray()
            .Select(item => new ReceiptTextMatchItem(
                item.GetProperty("name").GetString() ?? string.Empty,
                item.TryGetProperty("lineTotal", out var total) && total.ValueKind == JsonValueKind.Number ? total.GetDecimal() : null))
            .ToList();
    }

    /// <summary>Printed words whose centre lies under the outlines, in Azure's reading order.</summary>
    private static string TextUnder(IReadOnlyList<TextPolygon> polygons, List<ReceiptTextWord> words, ReceiptTextLayout layout)
    {
        var inside = words.Where(word =>
        {
            var x = (word.Polygon[0] + word.Polygon[2] + word.Polygon[4] + word.Polygon[6]) / 4 / layout.PageWidth;
            var y = (word.Polygon[1] + word.Polygon[3] + word.Polygon[5] + word.Polygon[7]) / 4 / layout.PageHeight;
            return polygons.Any(polygon => Contains(polygon, x, y));
        });
        return string.Join(' ', inside.Select(word => word.Text));
    }

    private static bool Contains(TextPolygon polygon, double x, double y)
    {
        var points = polygon.Points;
        var count = polygon.PointCount;
        var inside = false;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            double xi = points[i * 2], yi = points[i * 2 + 1], xj = points[j * 2], yj = points[j * 2 + 1];
            if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static string Verdict(List<(string Name, decimal LineTotal)> groundTruth, int k, string text)
    {
        var line = groundTruth[k];
        if (ReceiptTextSimilarity.PartialSimilarity(line.Name, text) >= ReceiptTextMatcher.NameMinimum)
        {
            return "right";
        }

        var others = groundTruth.Where((_, index) => index != k).ToList();
        var otherName = others.Any(other => ReceiptTextSimilarity.PartialSimilarity(other.Name, text) >= ReceiptTextMatcher.NameMinimum);
        if (ReceiptTextSimilarity.PricePrinted(line.LineTotal, text))
        {
            return otherName ? "ambiguous" : "right";
        }

        return otherName || others.Any(other => ReceiptTextSimilarity.PricePrinted(other.LineTotal, text)) ? "other-line" : "miss";
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PurchaseMemory.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

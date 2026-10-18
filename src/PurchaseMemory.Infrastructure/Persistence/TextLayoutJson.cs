using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Infrastructure.Persistence;

/// <summary>
/// Text columns for the printed-text geometry. Plain JSON in a text column
/// behaves the same on SQLite and Postgres; nothing ever queries inside it.
/// Short property names keep a receipt's layout around 20–40 KB.
/// </summary>
internal static class TextLayoutJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>A line's outlines: <c>[[x0,y0,x1,y1,…],…]</c>, <c>[]</c> when there are none.</summary>
    public static ValueConverter<IReadOnlyList<TextPolygon>, string> PolygonsConverter { get; } = new(
        polygons => JsonSerializer.Serialize(polygons.Select(polygon => polygon.Points), Options),
        json => ReadPolygons(json));

    public static ValueComparer<IReadOnlyList<TextPolygon>> PolygonsComparer { get; } = new(
        (left, right) => left != null && right != null && left.SequenceEqual(right),
        polygons => polygons.Aggregate(0, (hash, polygon) => HashCode.Combine(hash, polygon.GetHashCode())),
        polygons => polygons.ToArray());

    public static ValueConverter<IReadOnlyList<ReceiptTextLine>, string> LinesConverter { get; } = new(
        lines => JsonSerializer.Serialize(lines.Select(LineRecord.From).ToList(), Options),
        json => ReadLines(json));

    public static ValueComparer<IReadOnlyList<ReceiptTextLine>> LinesComparer { get; } = new(
        (left, right) => ReferenceEquals(left, right),
        lines => lines.Count,
        lines => lines);

    private static TextPolygon[] ReadPolygons(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return (JsonSerializer.Deserialize<List<double[]>>(json, Options) ?? [])
            .Select(points => TextPolygon.TryCreate(points))
            .OfType<TextPolygon>()
            .ToArray();
    }

    private static ReceiptTextLine[] ReadLines(string json) =>
        (JsonSerializer.Deserialize<List<LineRecord>>(json, Options) ?? [])
            .Select(line => new ReceiptTextLine(
                line.T ?? string.Empty,
                line.P ?? [],
                (line.W ?? []).Select(word => new ReceiptTextWord(word.T ?? string.Empty, word.P ?? [])).ToArray()))
            .ToArray();

    private sealed record WordRecord(string? T, double[]? P);

    private sealed record LineRecord(string? T, double[]? P, List<WordRecord>? W)
    {
        public static LineRecord From(ReceiptTextLine line) => new(
            line.Text,
            line.Polygon.ToArray(),
            line.Words.Select(word => new WordRecord(word.Text, word.Polygon.ToArray())).ToList());
    }
}

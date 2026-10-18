using PurchaseMemory.Domain.Receipts;

namespace PurchaseMemory.Application.Receipts.TextLocation;

/// <summary>What the matcher needs from a receipt line.</summary>
public sealed record ReceiptTextMatchItem(string Name, decimal? LineTotal);

/// <summary>The printed words found for one receipt line, grouped by OCR line in reading order.</summary>
public sealed record ReceiptTextMatch(int ItemIndex, IReadOnlyList<IReadOnlyList<ReceiptTextWord>> WordGroups);

/// <summary>
/// Finds each recognized line on the OCR text layout and turns its printed
/// words into highlight outlines. A port of the measured reference
/// (<c>highlight_study/analyze.py</c>: <c>hybrid_words</c> on the OCR line
/// layer only, <c>snake</c>, <c>render(kind="combo")</c> paddings), which
/// matched 110 of 110 lines on the household-2026-08 set.
/// </summary>
/// <remarks>
/// Lines are taken in receipt order and each OCR line is used once:
/// <list type="number">
/// <item>the unused OCR line whose text best contains the name (partial
/// similarity ≥ 0.6, first best wins); when that line does not print the
/// price, the nearest unused line just below it (within 2.5 line heights)
/// that does is added — Polish receipts often print the price under the name;</item>
/// <item>otherwise the first unused OCR line that prints the price;</item>
/// <item>otherwise nothing: the line keeps the recognition box.</item>
/// </list>
/// </remarks>
public static class ReceiptTextMatcher
{
    public const double NameMinimum = 0.6;
    public const double PriceRowGap = 2.5;

    /// <summary>Padding of each word outline along the text, in line heights (the "combo" style).</summary>
    public const double PadAlong = 0.30;

    /// <summary>Padding across the text, in line heights: receipt rows nearly touch.</summary>
    public const double PadAcross = 0.10;

    /// <summary>Outlines per item index, in photo fractions; items not found are absent.</summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<TextPolygon>> Locate(
        ReceiptTextLayout layout,
        IReadOnlyList<ReceiptTextMatchItem> items)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var result = new Dictionary<int, IReadOnlyList<TextPolygon>>();
        foreach (var match in FindWords(layout.Lines, items))
        {
            var polygons = Outline(match.WordGroups)
                .Select(points => TextPolygon.TryCreate(ToFractions(points, layout.PageWidth, layout.PageHeight)))
                .OfType<TextPolygon>()
                .ToList();
            if (polygons.Count > 0)
            {
                result[match.ItemIndex] = polygons;
            }
        }

        return result;
    }

    /// <summary>The printed words of every item that could be found.</summary>
    public static IReadOnlyList<ReceiptTextMatch> FindWords(
        IReadOnlyList<ReceiptTextLine> lines,
        IReadOnlyList<ReceiptTextMatchItem> items)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(items);

        var usable = lines.Where(line => line.Polygon.Count >= 8).ToList();
        var used = new HashSet<int>();
        var matches = new List<ReceiptTextMatch>();
        for (var index = 0; index < items.Count; index++)
        {
            var chosen = ChooseLines(items[index], usable, used);
            if (chosen.Count == 0)
            {
                continue;
            }

            used.UnionWith(chosen);
            var groups = chosen
                .Order()
                .Select(k => usable[k].Words.Where(word => word.Polygon.Count >= 8).ToList())
                .Where(words => words.Count > 0)
                .Select(ReadingOrder)
                .ToList();
            if (groups.Count > 0)
            {
                matches.Add(new ReceiptTextMatch(index, groups));
            }
        }

        return matches;
    }

    private static List<int> ChooseLines(ReceiptTextMatchItem item, List<ReceiptTextLine> lines, HashSet<int> used)
    {
        int? best = null;
        var bestSimilarity = NameMinimum;
        for (var k = 0; k < lines.Count; k++)
        {
            if (used.Contains(k))
            {
                continue;
            }

            var similarity = ReceiptTextSimilarity.PartialSimilarity(item.Name, lines[k].Text);
            if (similarity > bestSimilarity || (best is null && similarity >= bestSimilarity))
            {
                best = k;
                bestSimilarity = similarity;
            }
        }

        if (best is { } nameLine)
        {
            var chosen = new List<int> { nameLine };
            if (!ReceiptTextSimilarity.PricePrinted(item.LineTotal, lines[nameLine].Text))
            {
                var quad = Quad(lines[nameLine].Polygon);
                var height = Frame.Of(quad).Height;
                var top = quad.Min(point => point.Y);
                var below = Enumerable.Range(0, lines.Count)
                    .Where(k => !used.Contains(k) && k != nameLine)
                    .Select(k => (Index: k, Top: Quad(lines[k].Polygon).Min(point => point.Y)))
                    .Where(line => top < line.Top && line.Top <= top + PriceRowGap * Math.Max(height, 1)
                        && ReceiptTextSimilarity.PricePrinted(item.LineTotal, lines[line.Index].Text))
                    .ToList();
                if (below.Count > 0)
                {
                    chosen.Add(below.MinBy(line => line.Top).Index);
                }
            }

            return chosen;
        }

        for (var k = 0; k < lines.Count; k++)
        {
            if (!used.Contains(k) && ReceiptTextSimilarity.PricePrinted(item.LineTotal, lines[k].Text))
            {
                return [k];
            }
        }

        return [];
    }

    /// <summary>Words of one OCR line ordered along the direction of its first word.</summary>
    private static List<ReceiptTextWord> ReadingOrder(List<ReceiptTextWord> words)
    {
        var along = Frame.Of(Quad(words[0].Polygon)).U;
        return words.OrderBy(word => Dot(Centre(Quad(word.Polygon)), along)).ToList();
    }

    /// <summary>
    /// Per OCR line: each word padded, then every neighbouring pair joined by
    /// its convex hull. The union follows a tilted or bowed printed line.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Point>> Outline(IReadOnlyList<IReadOnlyList<ReceiptTextWord>> wordGroups)
    {
        ArgumentNullException.ThrowIfNull(wordGroups);
        var shapes = new List<IReadOnlyList<Point>>();
        foreach (var group in wordGroups)
        {
            var padded = group.Select(word => Padded(Quad(word.Polygon))).ToList();
            if (padded.Count == 1)
            {
                shapes.Add(padded[0]);
            }

            for (var index = 1; index < padded.Count; index++)
            {
                shapes.Add(ConvexHull([.. padded[index - 1], .. padded[index]]));
            }
        }

        return shapes;
    }

    private static Point[] Padded(Point[] quad)
    {
        var frame = Frame.Of(quad);
        var padAlong = PadAlong * frame.Height;
        var padAcross = PadAcross * frame.Height;
        (double U, double V)[] corners =
        [
            (frame.UMin - padAlong, frame.VMin - padAcross),
            (frame.UMax + padAlong, frame.VMin - padAcross),
            (frame.UMax + padAlong, frame.VMax + padAcross),
            (frame.UMin - padAlong, frame.VMax + padAcross)
        ];
        return corners
            .Select(corner => new Point(
                frame.Centre.X + frame.U.X * corner.U + frame.V.X * corner.V,
                frame.Centre.Y + frame.U.Y * corner.U + frame.V.Y * corner.V))
            .ToArray();
    }

    /// <summary>Andrew's monotone chain; counter-clockwise, no repeated end point.</summary>
    private static Point[] ConvexHull(Point[] points)
    {
        var sorted = points.Distinct().OrderBy(point => point.X).ThenBy(point => point.Y).ToArray();
        if (sorted.Length < 3)
        {
            return sorted;
        }

        var hull = new Point[sorted.Length * 2];
        var count = 0;
        foreach (var point in sorted)
        {
            while (count >= 2 && Cross(hull[count - 2], hull[count - 1], point) <= 0)
            {
                count--;
            }

            hull[count++] = point;
        }

        var lowerCount = count + 1;
        for (var index = sorted.Length - 2; index >= 0; index--)
        {
            while (count >= lowerCount && Cross(hull[count - 2], hull[count - 1], sorted[index]) <= 0)
            {
                count--;
            }

            hull[count++] = sorted[index];
        }

        return hull[..(count - 1)];
    }

    private static double Cross(Point o, Point a, Point b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

    private static double[] ToFractions(IReadOnlyList<Point> points, double width, double height)
    {
        var flat = new double[points.Count * 2];
        for (var index = 0; index < points.Count; index++)
        {
            flat[index * 2] = points[index].X / width;
            flat[index * 2 + 1] = points[index].Y / height;
        }

        return flat;
    }

    /// <summary>The first four corners of a flat pixel polygon (OCR words and lines are quads).</summary>
    internal static Point[] Quad(IReadOnlyList<double> polygon) =>
        [new(polygon[0], polygon[1]), new(polygon[2], polygon[3]), new(polygon[4], polygon[5]), new(polygon[6], polygon[7])];

    internal static Point Centre(Point[] points) =>
        new(points.Average(point => point.X), points.Average(point => point.Y));

    private static double Dot(Point a, Point b) => a.X * b.X + a.Y * b.Y;

    public readonly record struct Point(double X, double Y);

    /// <summary>
    /// Local frame of a quad: centre, unit U along the text (top and bottom edges
    /// averaged), unit V down across it, and the quad's extent along both.
    /// </summary>
    private readonly record struct Frame(Point Centre, Point U, Point V, double UMin, double UMax, double VMin, double VMax)
    {
        public double Height => VMax - VMin;

        public static Frame Of(Point[] quad)
        {
            var ux = quad[1].X - quad[0].X + (quad[2].X - quad[3].X);
            var uy = quad[1].Y - quad[0].Y + (quad[2].Y - quad[3].Y);
            var length = Math.Sqrt(ux * ux + uy * uy);
            if (length == 0)
            {
                length = 1;
            }

            var u = new Point(ux / length, uy / length);
            var v = new Point(-u.Y, u.X);
            var centre = ReceiptTextMatcher.Centre(quad);
            double uMin = double.MaxValue, uMax = double.MinValue, vMin = double.MaxValue, vMax = double.MinValue;
            foreach (var point in quad)
            {
                var offset = new Point(point.X - centre.X, point.Y - centre.Y);
                var along = Dot(offset, u);
                var across = Dot(offset, v);
                uMin = Math.Min(uMin, along);
                uMax = Math.Max(uMax, along);
                vMin = Math.Min(vMin, across);
                vMax = Math.Max(vMax, across);
            }

            return new Frame(centre, u, v, uMin, uMax, vMin, vMax);
        }
    }
}

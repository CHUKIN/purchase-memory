namespace PurchaseMemory.Application.Impressions;

/// <summary>
/// A purchase correction replaces all line rows of the purchase with new ones.
/// This decides which new line an impression on a removed line belongs to.
/// A line the editor said it continues (its saved id) wins, even if its name
/// or price changed. Otherwise the same text (case and surrounding spaces
/// ignored), preferring the same position and otherwise the nearest one. Each
/// new line takes at most one old line, so two identical rows keep their
/// impressions apart. Discounts are never rated and never matched.
/// </summary>
public static class ImpressionRelinking
{
    /// <param name="continuedLines">
    /// Removed line id → the new line id that continues it, as stated by the
    /// editor. Empty for callers that send a bare snapshot (MCP), which then
    /// rely on text matching alone.
    /// </param>
    public static IReadOnlyDictionary<Guid, Guid> Match(
        IReadOnlyCollection<ImpressionLineRef> removed,
        IReadOnlyCollection<ImpressionLineRef> added,
        IReadOnlyDictionary<Guid, Guid>? continuedLines = null)
    {
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(added);
        continuedLines ??= new Dictionary<Guid, Guid>();

        var available = added.Where(line => !line.IsDiscount).ToList();
        var result = new Dictionary<Guid, Guid>();
        var byText = new List<ImpressionLineRef>();
        foreach (var old in removed.Where(line => !line.IsDiscount).OrderBy(line => line.Position))
        {
            if (!continuedLines.TryGetValue(old.Id, out var nextId))
            {
                byText.Add(old);
                continue;
            }

            // A continued line never falls back to text: if it became a
            // discount, its impression stays on the purchase without a line.
            var next = available.FirstOrDefault(line => line.Id == nextId);
            if (next is not null)
            {
                result[old.Id] = next.Id;
                available.Remove(next);
            }
        }

        // Lines that continue a removed one are taken even if that line had no
        // impression, so a text match never steals them.
        var continuationTargets = continuedLines.Values.ToHashSet();
        available.RemoveAll(line => continuationTargets.Contains(line.Id));

        foreach (var old in byText)
        {
            var key = Normalize(old.Name);
            var match = available
                .Where(line => Normalize(line.Name) == key)
                .OrderBy(line => Math.Abs(line.Position - old.Position))
                .ThenBy(line => line.Position)
                .FirstOrDefault();
            if (match is null)
            {
                continue;
            }

            result[old.Id] = match.Id;
            available.Remove(match);
        }

        return result;
    }

    private static string Normalize(string name) => name.Trim().ToUpperInvariant();
}

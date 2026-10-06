namespace PurchaseMemory.Application.Impressions;

/// <summary>
/// A purchase correction replaces all line rows of the purchase with new ones.
/// This decides which new line an impression on a removed line belongs to:
/// the same text (case and surrounding spaces ignored), preferring the same
/// position and otherwise the nearest one. Each new line takes at most one old
/// line, so two identical rows keep their impressions apart. Discounts are
/// never rated and never matched.
/// </summary>
public static class ImpressionRelinking
{
    public static IReadOnlyDictionary<Guid, Guid> Match(
        IReadOnlyCollection<ImpressionLineRef> removed,
        IReadOnlyCollection<ImpressionLineRef> added)
    {
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(added);

        var available = added.Where(line => !line.IsDiscount).ToList();
        var result = new Dictionary<Guid, Guid>();
        foreach (var old in removed.Where(line => !line.IsDiscount).OrderBy(line => line.Position))
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

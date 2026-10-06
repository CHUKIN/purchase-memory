using System.Text;
using PurchaseMemory.Application.Products;

namespace PurchaseMemory.Application.Analytics;

public sealed record ValidatedAnalyticsPlan(AnalyticsPlan Plan, IReadOnlyList<AnalyticsNote> Notes);

/// <summary>
/// The server's check of every plan, whether the model or a person produced it.
/// Nothing unknown survives: ids (products, categories, tags) that do not exist, merchants and brands the
/// data has never seen, and impossible periods are dropped and reported as
/// notes, so the answer can say what it ignored instead of silently widening
/// or narrowing the question.
/// </summary>
public static class AnalyticsPlanValidator
{
    /// <summary>Periods before this year are certainly a misreading: there are no such receipts.</summary>
    public const int EarliestYear = 2000;

    /// <summary>Hard cap on raw input lists before anything reaches the database.</summary>
    public const int MaximumInputListLength = 200;

    public const int MaximumNotes = 20;

    /// <summary>
    /// Cuts every list of an untrusted plan to <see cref="MaximumInputListLength"/>
    /// entries, so an oversized request never turns into an oversized query.
    /// </summary>
    public static AnalyticsPlan CapInput(AnalyticsPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        static IReadOnlyList<T> Cap<T>(IReadOnlyList<T>? values) =>
            values is null ? [] : values.Count <= MaximumInputListLength ? values : values.Take(MaximumInputListLength).ToList();
        return plan with
        {
            ProductIds = Cap(plan.ProductIds),
            CategoryIds = Cap(plan.CategoryIds),
            NameTerms = Cap(plan.NameTerms),
            Brands = Cap(plan.Brands),
            Merchants = Cap(plan.Merchants),
            TagIds = Cap(plan.TagIds)
        };
    }

    public static ValidatedAnalyticsPlan Validate(
        AnalyticsPlan plan,
        AnalyticsVocabulary vocabulary,
        IReadOnlySet<Guid> existingProductIds,
        DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(vocabulary);
        ArgumentNullException.ThrowIfNull(existingProductIds);

        plan = CapInput(plan);
        var notes = new List<AnalyticsNote>();

        var metric = Enum.IsDefined(plan.Metric) ? plan.Metric : AnalyticsMetric.Spend;
        var groupBy = Enum.IsDefined(plan.GroupBy) ? plan.GroupBy : AnalyticsGroupBy.None;
        var discount = Enum.IsDefined(plan.Discount) ? plan.Discount : AnalyticsDiscountFilter.Any;

        var categoryIds = vocabulary.Categories.Select(category => category.Id).ToHashSet();
        var categories = KeepKnown(plan.CategoryIds, categoryIds.Contains, AnalyticsNote.UnknownCategory, notes);
        var products = KeepKnown(plan.ProductIds, existingProductIds.Contains, AnalyticsNote.UnknownProduct, notes);
        var merchants = Canonicalize(plan.Merchants, vocabulary.Merchants, AnalyticsNote.UnknownMerchant, notes);
        var brands = Canonicalize(plan.Brands, vocabulary.Brands, AnalyticsNote.UnknownBrand, notes);
        // A tag that does not exist is dropped like an unknown category: guessing would change the question.
        var tagIds = vocabulary.Tags.Select(tag => tag.Id).ToHashSet();
        var tags = KeepKnown(plan.TagIds, tagIds.Contains, AnalyticsNote.UnknownTag, notes);
        var terms = CleanTerms(plan.NameTerms, notes);

        var (from, to) = ValidatePeriod(plan.From, plan.To, today, notes);

        int? limit = null;
        if (metric == AnalyticsMetric.Top)
        {
            limit = plan.Limit ?? AnalyticsPlan.DefaultTopLimit;
            var clamped = Math.Clamp(limit.Value, 1, AnalyticsPlan.MaximumTopLimit);
            if (clamped != limit.Value)
            {
                notes.Add(new AnalyticsNote(AnalyticsNote.LimitClamped, limit.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            limit = clamped;
            if (groupBy is AnalyticsGroupBy.None or AnalyticsGroupBy.Month or AnalyticsGroupBy.Week)
            {
                // «Top» of months is a chart, not a ranking; products is what people mean.
                groupBy = AnalyticsGroupBy.Product;
            }
        }

        if (metric == AnalyticsMetric.Exists)
        {
            groupBy = AnalyticsGroupBy.None;
        }

        return new ValidatedAnalyticsPlan(
            new AnalyticsPlan
            {
                Metric = metric,
                ProductIds = products,
                CategoryIds = categories,
                NameTerms = terms,
                Brands = brands,
                TagIds = tags,
                Merchants = merchants,
                From = from,
                To = to,
                Discount = discount,
                GroupBy = groupBy,
                Limit = limit
            },
            DedupeNotes(notes));
    }

    public const int MaximumNotesPerCode = 3;

    /// <summary>
    /// Each distinct note once, at most <see cref="MaximumNotesPerCode"/> of one kind
    /// (so 200 unknown ids cannot hide an unknown store) and <see cref="MaximumNotes"/> in all.
    /// </summary>
    public static IReadOnlyList<AnalyticsNote> DedupeNotes(IEnumerable<AnalyticsNote> notes)
    {
        var perCode = new Dictionary<string, int>(StringComparer.Ordinal);
        return notes
            .Distinct()
            .Where(note => (perCode[note.Code] = perCode.GetValueOrDefault(note.Code) + 1) <= MaximumNotesPerCode)
            .Take(MaximumNotes)
            .ToList();
    }

    private static List<Guid> KeepKnown(
        IReadOnlyList<Guid>? values,
        Func<Guid, bool> exists,
        string code,
        List<AnalyticsNote> notes)
    {
        var kept = new List<Guid>();
        foreach (var value in (values ?? []).Distinct())
        {
            if (!exists(value))
            {
                notes.Add(new AnalyticsNote(code, value.ToString()));
            }
            else if (kept.Count >= AnalyticsPlan.MaximumListLength)
            {
                notes.Add(new AnalyticsNote(AnalyticsNote.TooManyValues, value.ToString()));
            }
            else
            {
                kept.Add(value);
            }
        }

        return kept;
    }

    /// <summary>Matches names the way receipts are matched (case, diacritics, punctuation) and returns the stored spelling.</summary>
    private static List<string> Canonicalize(
        IReadOnlyList<string>? values,
        IReadOnlyList<string> known,
        string code,
        List<AnalyticsNote> notes)
    {
        var byKey = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in known)
        {
            byKey.TryAdd(ProductNameNormalizer.Normalize(name), name);
        }

        var kept = new List<string>();
        foreach (var value in values ?? [])
        {
            var key = ProductNameNormalizer.Normalize(value);
            if (key.Length == 0)
            {
                continue;
            }

            if (!byKey.TryGetValue(key, out var canonical))
            {
                notes.Add(new AnalyticsNote(code, Shorten(value)));
            }
            else if (!kept.Contains(canonical, StringComparer.Ordinal))
            {
                if (kept.Count >= AnalyticsPlan.MaximumListLength)
                {
                    notes.Add(new AnalyticsNote(AnalyticsNote.TooManyValues, Shorten(value)));
                    continue;
                }

                kept.Add(canonical);
            }
        }

        return kept;
    }

    private static List<string> CleanTerms(IReadOnlyList<string>? values, List<AnalyticsNote> notes)
    {
        var kept = new List<string>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values ?? [])
        {
            var term = Shorten(value);
            var key = ProductNameNormalizer.Normalize(term);
            if (key.Length == 0)
            {
                notes.Add(new AnalyticsNote(AnalyticsNote.EmptyTermDropped));
                continue;
            }

            if (!keys.Add(key))
            {
                continue;
            }

            if (kept.Count >= AnalyticsPlan.MaximumListLength)
            {
                notes.Add(new AnalyticsNote(AnalyticsNote.TooManyValues, term));
                continue;
            }

            kept.Add(term);
        }

        return kept;
    }

    private static (DateOnly? From, DateOnly? To) ValidatePeriod(
        DateOnly? from,
        DateOnly? to,
        DateOnly today,
        List<AnalyticsNote> notes)
    {
        if (from is { } start && to is { } end && start > end)
        {
            (from, to) = (end, start);
            notes.Add(new AnalyticsNote(AnalyticsNote.PeriodSwapped));
        }

        var earliest = new DateOnly(EarliestYear, 1, 1);
        if (from > today || to < earliest)
        {
            // The whole period is impossible: there cannot be a single receipt in it.
            notes.Add(new AnalyticsNote(AnalyticsNote.PeriodOutOfRange));
            return (null, null);
        }

        if (from < earliest)
        {
            // Only the start is impossible: clamp it rather than widen the answer to all history.
            from = earliest;
            notes.Add(new AnalyticsNote(AnalyticsNote.PeriodOutOfRange));
        }

        if (to > today)
        {
            // «в сентябре» ends today; a later end would only look like missing data.
            to = today;
        }

        return (from, to);
    }

    /// <summary>One short line: a note or term never carries line breaks or an essay.</summary>
    public static string Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(text.Length, AnalyticsPlan.MaximumTermLength));
        foreach (var character in text.Trim())
        {
            if (builder.Length >= AnalyticsPlan.MaximumTermLength)
            {
                break;
            }

            builder.Append(char.IsControl(character) || character is '\u2028' or '\u2029' ? ' ' : character);
        }

        return builder.ToString().Trim();
    }
}

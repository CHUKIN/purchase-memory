using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.Infrastructure.Recognition;

public sealed class StructuredVisionProviderResolver : IStructuredVisionProviderResolver
{
    private readonly Dictionary<string, IStructuredVisionProvider> _providers;

    public StructuredVisionProviderResolver(IEnumerable<IStructuredVisionProvider> providers)
    {
        var providerList = providers.ToArray();
        var invalid = providerList.FirstOrDefault(provider => string.IsNullOrWhiteSpace(provider.Id));
        if (invalid is not null)
        {
            throw new InvalidOperationException(
                $"Structured vision provider '{invalid.GetType().Name}' has an empty identifier.");
        }

        var duplicate = providerList
            .GroupBy(provider => provider.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException(
                $"More than one structured vision provider is registered as '{duplicate.Key}'.");
        }

        _providers = providerList.ToDictionary(
            provider => provider.Id,
            StringComparer.OrdinalIgnoreCase);
    }

    public IStructuredVisionProvider Resolve(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return _providers.TryGetValue(providerId.Trim(), out var provider)
            ? provider
            : throw new InvalidOperationException(
                $"Structured vision provider '{providerId}' is not registered.");
    }
}

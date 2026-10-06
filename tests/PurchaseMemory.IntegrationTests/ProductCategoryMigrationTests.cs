using Microsoft.EntityFrameworkCore;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>Upgrading a database created before categories existed (SQLite, like the owner's).</summary>
public sealed class ProductCategoryMigrationTests
{
    private const string MigrationBeforeCategories = "20260924154254_AddReceiptActorIdentity";

    [Fact]
    public async Task ExistingProductsBecomeUnclassifiedAndTheTreeIsSeeded()
    {
        var cancel = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), "purchase-memory-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<PurchaseMemoryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "migration.db")};Pooling=False")
                .Options;
            var productId = Guid.NewGuid();
            var purchaseId = Guid.NewGuid();
            var aliasId = Guid.NewGuid();
            await using (var oldContext = new PurchaseMemoryDbContext(options))
            {
                await oldContext.Database.MigrateAsync(MigrationBeforeCategories, cancel);
                await oldContext.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO products (Id, CanonicalName, NormalizedName, CreatedAt, UpdatedAt)
                    VALUES ({productId}, {"Mleko 3,2%"}, {"mleko 3 2"}, {1_785_758_400_000L}, {1_785_758_400_000L});
                    INSERT INTO product_aliases (Id, ProductId, MerchantKey, NormalizedText, RawText, Source, CreatedAt)
                    VALUES ({aliasId}, {productId}, {"lidl"}, {"mleko 3 2"}, {"MLEKO 3,2%"}, {"AutoCreated"}, {1_785_758_400_000L});
                    INSERT INTO purchase_drafts (Id, CreatedAt, UpdatedAt, MerchantName, ReceiptTotal)
                    VALUES ({purchaseId}, {1_785_758_400_000L}, {1_785_758_400_000L}, {"Lidl"}, {"4.49"});
                    INSERT INTO purchase_items (Id, PurchaseDraftId, Position, Name, Quantity, UnitPrice, LineTotal, ProductId)
                    VALUES ({Guid.NewGuid()}, {purchaseId}, {0}, {"Mleko 3,2%"}, {"1"}, {"4.49"}, {"4.49"}, {productId});
                    """, cancel);
            }

            await using var upgraded = new PurchaseMemoryDbContext(options);
            await upgraded.Database.MigrateAsync(cancel);

            var product = await upgraded.Products.SingleAsync(entry => entry.Id == productId, cancel);
            Assert.Equal("Mleko 3,2%", product.CanonicalName);
            Assert.Null(product.CategoryId);
            Assert.Null(product.CategorySource);
            Assert.Null(product.Brand);
            Assert.Null(product.BrandSource);
            Assert.Null(product.CategorizationAttemptedAt);
            var alias = await upgraded.ProductAliases.SingleAsync(entry => entry.Id == aliasId, cancel);
            Assert.Equal((productId, "MLEKO 3,2%"), (alias.ProductId, alias.RawText));
            Assert.Equal(ProductCategorySeed.Categories.Count, await upgraded.ProductCategories.CountAsync(cancel));
            Assert.Equal(1, await upgraded.Set<PurchaseMemory.Domain.Purchases.PurchaseItem>()
                .CountAsync(item => item.ProductId == productId, cancel));

            // The upgraded row accepts a classification like any new one.
            product.ConfirmCategory(ProductCategorySeed.IdFor(2), DateTimeOffset.UtcNow);
            await upgraded.SaveChangesAsync(cancel);
            Assert.False(upgraded.Database.HasPendingModelChanges());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

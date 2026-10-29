using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Infrastructure.Persistence;
using PurchaseMemory.Infrastructure.Processing;

namespace PurchaseMemory.IntegrationTests;

/// <summary>Category tree, classification precedence and AI suggestions. Runs on SQLite and, opted in, Postgres.</summary>
public sealed class ProductCategoryTests
{
    private static readonly ReceiptActor Anna = new("user-anna", "anna@example.com");
    private static readonly Guid Dairy = ProductCategorySeed.IdFor(1);
    private static readonly Guid Milk = ProductCategorySeed.IdFor(2);
    private static readonly Guid Cheese = ProductCategorySeed.IdFor(3);
    private static readonly Guid Drinks = ProductCategorySeed.IdFor(33);
    private static readonly Guid Coffee = ProductCategorySeed.IdFor(34);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task FreshDatabaseHasTheTwoLevelBaseTree()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var tree = await scope.ServiceProvider.GetRequiredService<IProductCategoryService>().GetTreeAsync(Cancel);

        var groups = tree.Where(category => category.ParentId is null).ToList();
        Assert.Equal(12, groups.Count);
        Assert.Equal("Молочное и яйца", groups[0].Name);
        Assert.Equal("other", groups[^1].SystemKey);
        Assert.Equal(ProductCategorySeed.Categories.Count, tree.Count);
        var groupIds = groups.Select(group => group.Id).ToHashSet();
        Assert.All(tree.Where(category => category.ParentId is not null), child => Assert.Contains(child.ParentId!.Value, groupIds));
        Assert.Equal(tree.Count, tree.Select(category => category.SystemKey).Distinct().Count());
        Assert.Contains(tree, category => category.Name == "Кофе" && category.ParentId == Drinks);

        // Parents come first, each followed by its own children.
        Assert.Equal(Milk, tree[1].Id);
    }

    [Fact]
    public async Task HumanCategoryIsAuthoritativeAndSuggestionsNeverOverwriteIt()
    {
        await using var application = await TestApplication.CreateAsync();
        var productId = await SaveProductAsync(application, "MLEKO UHT 3,2% LACIATE");
        var categorizer = new FakeCategorizer((products, _) =>
            products.Select(product => new ProductCategorySuggestion(product.ProductId, Cheese, "Łaciate")).ToList());

        var first = await RunCategorizationAsync(application, categorizer, [productId]);
        Assert.Equal(1, first.Suggested);
        var suggested = await ReadProductAsync(application, productId);
        Assert.Equal((Cheese, ProductAttributeSource.Suggested), (suggested.CategoryId, suggested.CategorySource));
        Assert.Equal(("Łaciate", ProductAttributeSource.Suggested), (suggested.Brand, suggested.BrandSource));

        await using (var scope = application.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();
            await service.ClassifyProductAsync(new ClassifyProductCommand(productId, true, Milk, false, null), Anna, Cancel);
        }

        // Neither the post-save path nor the backfill touches it again.
        var second = await RunCategorizationAsync(application, categorizer, [productId]);
        var backfill = await RunBackfillAsync(application, categorizer);
        Assert.Equal((0, 0), (second.Considered, backfill.Considered));

        var confirmed = await ReadProductAsync(application, productId);
        Assert.Equal((Milk, ProductAttributeSource.Confirmed), (confirmed.CategoryId, confirmed.CategorySource));
        // The brand was not part of the correction, so it is still the suggestion.
        Assert.Equal(("Łaciate", ProductAttributeSource.Suggested), (confirmed.Brand, confirmed.BrandSource));

        await using (var scope = application.Services.CreateAsyncScope())
        {
            var audit = await scope.ServiceProvider.GetRequiredService<IProductCategoryService>()
                .GetAuditTrailAsync(productId, Cancel);
            Assert.Contains(audit, entry => entry.Source == ProductAuditChangeSource.Human
                && entry.OldValue == "Сыр (suggested)" && entry.NewValue == "Молоко" && entry.Actor == "user-anna");
            Assert.Contains(audit, entry => entry.Source == ProductAuditChangeSource.Suggestion
                && entry.FieldName == ProductAuditEntry.BrandField && entry.Actor == "ai:fake-model");
        }
    }

    [Fact]
    public void DomainGuardsConfirmedValuesOnItsOwn()
    {
        var now = DateTimeOffset.UnixEpoch;
        var product = Product.Create("Kawa", "kawa", now);
        product.ConfirmCategory(null, now);
        product.ConfirmBrand("Jacobs", "jacobs", now);

        Assert.False(product.ApplySuggestion(Coffee, "Tchibo", "tchibo", now));
        Assert.Null(product.CategoryId);
        Assert.Equal("Jacobs", product.Brand);
    }

    [Fact]
    public async Task CategoryBelongsToTheProductSoEveryPurchaseIsCovered()
    {
        await using var application = await TestApplication.CreateAsync();
        var productId = await SaveProductAsync(application, "Kawa ziarnista Lavazza 1kg");
        var again = await SaveProductAsync(application, "Kawa ziarnista Lavazza 1kg");
        Assert.Equal(productId, again);

        await using (var scope = application.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IProductCategoryService>().ClassifyProductAsync(
                new ClassifyProductCommand(productId, true, Coffee, true, "  Lavazza "), Anna, Cancel);
        }

        await using (var scope = application.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<IProductCatalogService>();
            // A group includes its subcategories.
            var inDrinks = await catalog.GetCatalogAsync(new ProductCatalogQuery { CategoryId = Drinks }, Cancel);
            var entry = Assert.Single(inDrinks.Items);
            Assert.Equal((productId, 2, "Lavazza"), (entry.ProductId, entry.PurchaseCount, entry.Brand));

            var byBrand = await catalog.GetCatalogAsync(new ProductCatalogQuery { Brand = "lavaz" }, Cancel);
            Assert.Single(byBrand.Items);
            var uncategorized = await catalog.GetCatalogAsync(new ProductCatalogQuery { Uncategorized = true }, Cancel);
            Assert.Empty(uncategorized.Items);
            Assert.Empty((await catalog.GetCatalogAsync(new ProductCatalogQuery { CategoryId = Dairy }, Cancel)).Items);

            var detail = await catalog.GetProductAsync(productId, Cancel);
            Assert.Equal((Coffee, ProductAttributeSource.Confirmed), (detail!.CategoryId, detail.CategorySource));

            await Assert.ThrowsAsync<ProductCategoryNotFoundException>(() =>
                catalog.GetCatalogAsync(new ProductCatalogQuery { CategoryId = Guid.NewGuid() }, Cancel));
        }
    }

    [Fact]
    public async Task DeletingASubcategoryMovesItsProductsToTheParent()
    {
        await using var application = await TestApplication.CreateAsync();
        var productId = await SaveProductAsync(application, "Ser Gouda plastry");
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();
            await service.ClassifyProductAsync(new ClassifyProductCommand(productId, true, Cheese, false, null), Anna, Cancel);
            var deletion = await service.DeleteAsync(Cheese, Anna, Cancel);
            Assert.Equal((Dairy, 1), (deletion.MovedToCategoryId, deletion.MovedProductCount));
        }

        var moved = await ReadProductAsync(application, productId);
        Assert.Equal((Dairy, ProductAttributeSource.Confirmed), (moved.CategoryId, moved.CategorySource));
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();
            Assert.DoesNotContain(await service.GetTreeAsync(Cancel), category => category.Id == Cheese);
            var audit = await service.GetAuditTrailAsync(productId, Cancel);
            Assert.Contains(audit, entry => entry.Source == ProductAuditChangeSource.CategoryDeleted
                && entry.OldValue == "Сыр" && entry.NewValue == "Молочное и яйца");
        }
    }

    [Fact]
    public async Task DeletingAGroupIsRefusedWhileItHasSubcategoriesAndEmptiesProductsOtherwise()
    {
        await using var application = await TestApplication.CreateAsync();
        var productId = await SaveProductAsync(application, "Karma dla kota");
        await using var scope = application.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();

        await Assert.ThrowsAsync<ProductCategoryConflictException>(() => service.DeleteAsync(Dairy, Anna, Cancel));

        var own = await service.CreateAsync("  Питомцы ", null, Cancel);
        Assert.Equal(("Питомцы", null), (own.Name, own.SystemKey));
        await service.ClassifyProductAsync(new ClassifyProductCommand(productId, true, own.Id, false, null), Anna, Cancel);
        await service.DeleteAsync(own.Id, Anna, Cancel);

        var product = await ReadProductAsync(application, productId);
        // Back to «nobody decided», so a later suggestion run may classify it.
        Assert.Equal((null, null), (product.CategoryId, product.CategorySource));
    }

    [Fact]
    public async Task UsersAddAndRenameCategoriesWithinTwoLevels()
    {
        await using var application = await TestApplication.CreateAsync();
        await using var scope = application.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();

        var sub = await service.CreateAsync("Кокосовое молоко", Dairy, Cancel);
        Assert.Equal(Dairy, sub.ParentId);
        await Assert.ThrowsAsync<ProductCategoryConflictException>(() => service.CreateAsync("Третий уровень", sub.Id, Cancel));
        await Assert.ThrowsAsync<ProductCategoryConflictException>(() => service.CreateAsync("молоко", Dairy, Cancel));
        await Assert.ThrowsAsync<ProductCategoryNotFoundException>(() => service.CreateAsync("X", Guid.NewGuid(), Cancel));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new string('я', 101), null, Cancel));

        var renamed = await service.RenameAsync(Milk, "Молоко и напитки", Cancel);
        // A renamed seed is the household's own now; the translated label would hide the new name.
        Assert.Equal(("Молоко и напитки", null), (renamed.Name, renamed.SystemKey));
        await Assert.ThrowsAsync<ProductCategoryConflictException>(() => service.RenameAsync(Cheese, "Молоко и напитки", Cancel));
    }

    [Fact]
    public async Task InventedCategoryIdsAreIgnoredAndFailuresNeverEscape()
    {
        await using var application = await TestApplication.CreateAsync();
        var first = await SaveProductAsync(application, "Chleb żytni");
        var second = await SaveProductAsync(application, "Woda mineralna 1,5l");

        var inventing = new FakeCategorizer((products, _) =>
        [
            new ProductCategorySuggestion(products.Single(product => product.Name.StartsWith("Chleb", StringComparison.Ordinal)).ProductId, Guid.NewGuid(), null),
            new ProductCategorySuggestion(Guid.NewGuid(), Milk, "Stranger"),
            new ProductCategorySuggestion(products.Single(product => product.Name.StartsWith("Woda", StringComparison.Ordinal)).ProductId, ProductCategorySeed.IdFor(37), null)
        ]);
        var summary = await RunCategorizationAsync(application, inventing, [first, second]);
        Assert.Equal((2, 1, 0), (summary.Considered, summary.Suggested, summary.FailedBatches));
        Assert.Null((await ReadProductAsync(application, first)).CategorySource);
        Assert.Equal(ProductCategorySeed.IdFor(37), (await ReadProductAsync(application, second)).CategoryId);

        // The model answered for «Chleb» (with an invented id), so it is not sent again by default.
        var failing = new FakeCategorizer((_, _) => throw new ProductCategorizationException("model down"));
        Assert.Equal(0, (await RunBackfillAsync(application, failing)).Considered);
        // A retry also asks again for names the model left empty, so «Woda» (classified, unnamed) comes too.
        var failed = await RunBackfillAsync(application, failing, retryAttempted: true);
        Assert.Equal((2, 0, 1), (failed.Considered, failed.Suggested, failed.FailedBatches));
        // A failed batch is not an answer: the product stays eligible.
        Assert.Null((await ReadProductAsync(application, first)).CategorySource);
        Assert.Equal(2, (await RunBackfillAsync(application, failing, retryAttempted: true)).Considered);

        var unconfigured = new FakeCategorizer((_, _) => throw new InvalidOperationException("must not be called"))
        {
            IsConfigured = false
        };
        Assert.False((await RunBackfillAsync(application, unconfigured)).Configured);
    }

    [Fact]
    public async Task BatchesFollowTheCategorizerLimit()
    {
        await using var application = await TestApplication.CreateAsync();
        var ids = new List<Guid>();
        foreach (var name in new[] { "Jabłka", "Banany", "Gruszki", "Śliwki", "Cytryny" })
        {
            ids.Add(await SaveProductAsync(application, name));
        }

        var calls = new List<int>();
        var categorizer = new FakeCategorizer((products, categories) =>
        {
            calls.Add(products.Count);
            Assert.Contains(categories, option => option.Path == "Овощи и фрукты / Фрукты");
            return products.Select(product => new ProductCategorySuggestion(product.ProductId, ProductCategorySeed.IdFor(15), null)).ToList();
        }) { MaximumBatchSize = 2 };

        var summary = await RunBackfillAsync(application, categorizer);
        Assert.Equal([2, 2, 1], calls);
        Assert.Equal((5, 5, 6, 9), (summary.Suggested, summary.Considered, summary.InputTokens, summary.OutputTokens));
    }

    [Fact]
    public async Task HumanConfirmationDuringTheModelCallSurvives()
    {
        await using var application = await TestApplication.CreateAsync();
        var productId = await SaveProductAsync(application, "Ser Gouda plastry 150g");

        // The person confirms while the model is «thinking», after the snapshot was read.
        var categorizer = new FakeCategorizer((products, _) =>
        {
            using var scope = application.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IProductCategoryService>()
                .ClassifyProductAsync(new ClassifyProductCommand(productId, true, Cheese, true, "Hochland"), Anna, Cancel)
                .GetAwaiter().GetResult();
            return products.Select(product => new ProductCategorySuggestion(product.ProductId, Milk, "Mlekovita")).ToList();
        });

        var summary = await RunCategorizationAsync(application, categorizer, [productId]);

        Assert.Equal((1, 0), (summary.Considered, summary.Suggested));
        var product = await ReadProductAsync(application, productId);
        Assert.Equal((Cheese, ProductAttributeSource.Confirmed), (product.CategoryId, product.CategorySource));
        Assert.Equal(("Hochland", ProductAttributeSource.Confirmed), (product.Brand, product.BrandSource));
        Assert.NotNull(product.CategorizationAttemptedAt);
        await using var read = application.Services.CreateAsyncScope();
        var audit = await read.ServiceProvider.GetRequiredService<IProductCategoryService>().GetAuditTrailAsync(productId, Cancel);
        Assert.DoesNotContain(audit, entry => entry.Source == ProductAuditChangeSource.Suggestion);
    }

    [Fact]
    public async Task AnsweredProductsAreNotPaidForAgainUnlessRetried()
    {
        await using var application = await TestApplication.CreateAsync();
        var unplaced = await SaveProductAsync(application, "Artykuł nieznany XYZ");
        var brandOnly = await SaveProductAsync(application, "Tymbark 1L");
        var calls = 0;
        var categorizer = new FakeCategorizer((products, _) =>
        {
            calls++;
            return products
                .Select(product => new ProductCategorySuggestion(
                    product.ProductId, null, product.ProductId == brandOnly ? "Tymbark" : null))
                .ToList();
        });

        await RunCategorizationAsync(application, categorizer, [unplaced, brandOnly]);
        Assert.Equal(1, calls);
        Assert.NotNull((await ReadProductAsync(application, unplaced)).CategorizationAttemptedAt);
        Assert.Equal("Tymbark", (await ReadProductAsync(application, brandOnly)).Brand);

        // Neither the post-save path nor the default backfill asks again.
        Assert.Equal(0, (await RunCategorizationAsync(application, categorizer, [unplaced, brandOnly])).Considered);
        Assert.Equal(0, (await RunBackfillAsync(application, categorizer)).Considered);
        Assert.Equal(1, calls);

        Assert.Equal(2, (await RunBackfillAsync(application, categorizer, retryAttempted: true)).Considered);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void DispatcherNeverBlocksTheSavePath()
    {
        var dispatcher = new ChannelProductCategorizationDispatcher();
        for (var index = 0; index < 1_000; index++)
        {
            dispatcher.Enqueue([Guid.NewGuid()]);
        }
    }

    private static async Task<Guid> SaveProductAsync(TestApplication application, string itemName)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>();
        await service.CreateAsync(
            new CreatePurchaseDraftCommand(
                "Biedronka", null, "PLN", null,
                [new PurchaseItemCommand(itemName, 1, "szt", 9.99m, 9.99m)],
                AllowPossibleDuplicate: true),
            Cancel);
        var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var normalized = ProductNameNormalizer.Normalize(itemName);
        return (await context.Products.AsNoTracking().SingleAsync(product => product.NormalizedName == normalized, Cancel)).Id;
    }

    private static async Task<Product> ReadProductAsync(TestApplication application, Guid productId)
    {
        await using var scope = application.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        return await context.Products.AsNoTracking().SingleAsync(product => product.Id == productId, Cancel);
    }

    private static ProductCategorizationService CreateService(AsyncServiceScope scope, IProductCategorizer categorizer) =>
        new(
            scope.ServiceProvider.GetRequiredService<IProductCategoryRepository>(),
            categorizer,
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            NullLogger<ProductCategorizationService>.Instance);

    private static async Task<ProductCategorizationRunSummary> RunCategorizationAsync(
        TestApplication application,
        IProductCategorizer categorizer,
        IReadOnlyCollection<Guid> productIds)
    {
        await using var scope = application.Services.CreateAsyncScope();
        return await CreateService(scope, categorizer).SuggestForProductsAsync(productIds, Cancel);
    }

    private static async Task<ProductCategorizationRunSummary> RunBackfillAsync(
        TestApplication application,
        IProductCategorizer categorizer,
        bool retryAttempted = false)
    {
        await using var scope = application.Services.CreateAsyncScope();
        return await CreateService(scope, categorizer).SuggestForUnclassifiedAsync(100, retryAttempted, Cancel);
    }

    private sealed class FakeCategorizer(
        Func<IReadOnlyList<ProductToCategorize>, IReadOnlyList<CategoryOption>, IReadOnlyList<ProductCategorySuggestion>> respond)
        : IProductCategorizer
    {
        public bool IsConfigured { get; init; } = true;

        public string ModelName => "fake-model";

        public int MaximumBatchSize { get; init; } = 40;

        public Task<ProductCategorizationResult> SuggestAsync(
            IReadOnlyList<ProductToCategorize> products,
            IReadOnlyList<CategoryOption> categories,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProductCategorizationResult(respond(products, categories), 2, 3));
    }
}

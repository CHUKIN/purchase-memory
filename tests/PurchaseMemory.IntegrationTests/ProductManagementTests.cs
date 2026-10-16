using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Analytics;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Tags;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Application.ReviewQueue;
using PurchaseMemory.Domain.Impressions;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Domain.Purchases;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Products a person manages directly: create by hand or from a barcode,
/// rename, merge two products, settle a «проверить» flag. Service level on
/// SQLite (Postgres when opted in) and through the real host with real auth.
/// </summary>
public sealed class ProductManagementTests
{
    private static readonly ReceiptActor Anna = new("user-anna", "anna@example.com");
    private static readonly Guid Milk = ProductCategorySeed.IdFor(2);
    private static readonly Guid Cheese = ProductCategorySeed.IdFor(3);
    private static readonly DateTimeOffset July1 = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MergeMovesLinesAliasesTagsImpressionsAndHistoryAndDeletesTheSource()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        await SaveAsync(services, "Biedronka", ("ACTIV KEFI BOR 280", 3.49m));
        await SaveAsync(services, "Biedronka", ("ACTIV KEFI NAT 280", 3.59m));
        await SaveAsync(services, "Lidl", ("ACTIV KEFI NAT 280", 3.79m));
        var target = await FindProductAsync(services, "ACTIV KEFI BOR 280");
        var source = await FindProductAsync(services, "ACTIV KEFI NAT 280");
        Assert.True(source.NeedsReview);

        var tags = await CreateTagsAsync(services, "Завтрак", "Для ребёнка", "Акция");
        await SetTagsAsync(services, target.Id, tags[0], tags[2]);
        await SetTagsAsync(services, source.Id, tags[1], tags[2]);
        var impressionId = await AddImpressionAsync(services, source.Id);
        await ClassifyAsync(services, source.Id, Cheese, "Activia");
        var spendBefore = await SpendAsync(application);
        var linesBefore = await CountLinesAsync(services);

        var result = await MergeAsync(services, source.Id, target.Id);

        Assert.Equal((source.Id, target.Id), (result.SourceProductId, result.TargetProductId));
        Assert.Equal(2, result.MovedLineCount);
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        Assert.False(await dbContext.Products.AnyAsync(product => product.Id == source.Id, Cancel));
        var merged = await dbContext.Products.SingleAsync(product => product.Id == target.Id, Cancel);
        Assert.False(merged.NeedsReview);
        Assert.Equal("ACTIV KEFI BOR 280", merged.CanonicalName);

        // Every line of both products now belongs to the target.
        var lines = await dbContext.Set<PurchaseItem>().ToListAsync(Cancel);
        Assert.All(lines, line => Assert.Equal(target.Id, line.ProductId));
        Assert.Equal(linesBefore, lines.Count);

        // Aliases follow (both stores), and the merged wording is a person's decision now.
        var aliases = await dbContext.ProductAliases.Where(alias => alias.ProductId == target.Id).ToListAsync(Cancel);
        Assert.Equal(3, aliases.Count);
        Assert.All(
            aliases.Where(alias => alias.RawText == "ACTIV KEFI NAT 280"),
            alias => Assert.Equal(ProductAliasSource.HumanConfirmed, alias.Source));
        Assert.Equal(ProductAliasSource.AutoCreated, aliases.Single(alias => alias.RawText == "ACTIV KEFI BOR 280").Source);

        // Tags are united without duplicates.
        var tagIds = await dbContext.ProductTagLinks.Where(link => link.ProductId == target.Id).Select(link => link.TagId).ToListAsync(Cancel);
        Assert.Equal(tags.Order(), tagIds.Order());
        Assert.False(await dbContext.ProductTagLinks.AnyAsync(link => link.ProductId == source.Id, Cancel));

        // The target had no confirmed category or brand, so the source's confirmed ones win.
        Assert.Equal((Cheese, ProductAttributeSource.Confirmed), (merged.CategoryId, merged.CategorySource));
        Assert.Equal(("Activia", ProductAttributeSource.Confirmed), (merged.Brand, merged.BrandSource));

        // Impressions and the source's audit history move; the merge entry names both.
        var impression = await dbContext.ProductImpressions.SingleAsync(entry => entry.Id == impressionId, Cancel);
        Assert.Equal(target.Id, impression.ProductId);
        var audit = await dbContext.ProductAuditEntries.Where(entry => entry.ProductId == target.Id).ToListAsync(Cancel);
        var merge = Assert.Single(audit, entry => entry.FieldName == ProductAuditEntry.MergeField);
        Assert.Contains(source.Id.ToString("D"), merge.OldValue, StringComparison.Ordinal);
        Assert.Contains("ACTIV KEFI NAT 280", merge.OldValue, StringComparison.Ordinal);
        Assert.Contains(target.Id.ToString("D"), merge.NewValue, StringComparison.Ordinal);
        Assert.Equal(Anna.UserId, merge.Actor);
        Assert.Contains(audit, entry => entry.FieldName == ProductAuditEntry.ReviewField && entry.Source == ProductAuditChangeSource.Matching);
        Assert.False(await dbContext.ProductAuditEntries.AnyAsync(entry => entry.ProductId == source.Id, Cancel));
        Assert.Empty(await dbContext.ProductReviewCandidates.ToListAsync(Cancel));

        // A merge moves money between products, never in or out of the totals.
        Assert.Equal(spendBefore, await SpendAsync(application));

        // The next receipt with the merged wording goes straight to the target.
        await SaveAsync(services, "Biedronka", ("ACTIV KEFI NAT 280", 3.59m));
        Assert.Equal(1, await dbContext.Products.CountAsync(Cancel));
    }

    [Fact]
    public async Task MergeKeepsTheTargetsConfirmedClassificationAndCapsTags()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        var target = await CreateAsync(services, new CreateProductCommand("Mleko 2%", "Łaciate", Milk));
        var source = await CreateAsync(services, new CreateProductCommand("Mleko UHT", "Mlekovita", Cheese));
        var tags = await CreateTagsAsync(services, Enumerable.Range(1, 22).Select(index => $"Тег {index:00}").ToArray());
        await SetTagsAsync(services, target.Id, tags[..15]);
        await SetTagsAsync(services, source.Id, tags[10..]);

        await MergeAsync(services, source.Id, target.Id);

        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var merged = await dbContext.Products.SingleAsync(product => product.Id == target.Id, Cancel);
        Assert.Equal((Milk, "Łaciate"), (merged.CategoryId, merged.Brand));
        // The target's own 15 stay; the source fills the room up to the limit.
        var tagIds = await dbContext.ProductTagLinks.Where(link => link.ProductId == target.Id).Select(link => link.TagId).ToListAsync(Cancel);
        Assert.Equal(ProductTagLink.MaximumTagsPerProduct, tagIds.Count);
        Assert.All(tags[..15], tag => Assert.Contains(tag, tagIds));
    }

    [Fact]
    public async Task MergeRedirectsOtherFlaggedProductsCandidates()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        var duplicate = await CreateAsync(services, new CreateProductCommand("Ser Gouda"));
        var keeper = await CreateAsync(services, new CreateProductCommand("Ser Gouda Hochland"));
        var flagged = await CreateAsync(services, new CreateProductCommand("Ser Gouda 250"));
        await using (var scope = services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            var product = await dbContext.Products.SingleAsync(entry => entry.Id == flagged.Id, Cancel);
            product.FlagForReview(ProductReviewReason.AmbiguousMatch, July1);
            dbContext.ProductReviewCandidates.AddRange(
                ProductReviewCandidate.Create(flagged.Id, 0, duplicate.Id, 0.8),
                ProductReviewCandidate.Create(flagged.Id, 1, keeper.Id, 0.7));
            await dbContext.SaveChangesAsync(Cancel);
        }

        await MergeAsync(services, duplicate.Id, keeper.Id);

        await using var check = services.CreateAsyncScope();
        var catalog = check.ServiceProvider.GetRequiredService<IProductCatalogService>();
        var detail = await catalog.GetProductAsync(flagged.Id, Cancel);
        // The merged-away candidate is not offered twice: only the target remains.
        var candidate = Assert.Single(detail!.ReviewCandidates!);
        Assert.Equal(keeper.Id, candidate.ProductId);
        Assert.True(detail.NeedsReview);
    }

    [Fact]
    public async Task MergeRefusesItselfAndUnknownProductsAndASecondMergeIsNotFound()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        var first = await CreateAsync(services, new CreateProductCommand("Chleb"));
        var second = await CreateAsync(services, new CreateProductCommand("Chleb żytni"));

        await Assert.ThrowsAsync<ArgumentException>(() => MergeAsync(services, first.Id, first.Id));
        await Assert.ThrowsAsync<ProductNotFoundException>(() => MergeAsync(services, Guid.NewGuid(), second.Id));
        await Assert.ThrowsAsync<ProductNotFoundException>(() => MergeAsync(services, first.Id, Guid.NewGuid()));

        await MergeAsync(services, first.Id, second.Id);
        // Repeating the same merge finds nothing to move, and nothing changes.
        await Assert.ThrowsAsync<ProductNotFoundException>(() => MergeAsync(services, first.Id, second.Id));
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        Assert.Equal(second.Id, (await dbContext.Products.SingleAsync(Cancel)).Id);
    }

    [Fact]
    public async Task ConfirmDistinctClearsTheFlagConfirmsTheAliasAndDropsTheCandidates()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        await SaveAsync(services, "Biedronka", ("ACTIV KEFI BOR 280", 3.49m));
        await SaveAsync(services, "Biedronka", ("ACTIV KEFI NAT 280", 3.59m));
        var flagged = await FindProductAsync(services, "ACTIV KEFI NAT 280");

        await using (var scope = services.CreateAsyncScope())
        {
            var confirmed = await scope.ServiceProvider.GetRequiredService<IProductManagementService>()
                .ConfirmDistinctAsync(flagged.Id, Anna, Cancel);
            Assert.False(confirmed.NeedsReview);
            Assert.Null(confirmed.ReviewReason);
            // Repeating it is harmless.
            await scope.ServiceProvider.GetRequiredService<IProductManagementService>()
                .ConfirmDistinctAsync(flagged.Id, Anna, Cancel);
        }

        await using var check = services.CreateAsyncScope();
        var dbContext = check.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        Assert.Equal(2, await dbContext.Products.CountAsync(Cancel));
        Assert.Empty(await dbContext.ProductReviewCandidates.ToListAsync(Cancel));
        var alias = await dbContext.ProductAliases.SingleAsync(entry => entry.ProductId == flagged.Id, Cancel);
        Assert.Equal(ProductAliasSource.HumanConfirmed, alias.Source);
        var audit = await dbContext.ProductAuditEntries
            .Where(entry => entry.ProductId == flagged.Id && entry.FieldName == ProductAuditEntry.ReviewField)
            .ToListAsync(Cancel);
        Assert.Equal(2, audit.Count);
        Assert.Single(audit, entry => entry.NewValue == ProductManagementService.DistinctValue && entry.Actor == Anna.UserId);
        Assert.Null(Find(await QueueAsync(check), ReviewQueueItemKind.ProductNeedsReview));
        await Assert.ThrowsAsync<ProductNotFoundException>(() => check.ServiceProvider
            .GetRequiredService<IProductManagementService>().ConfirmDistinctAsync(Guid.NewGuid(), Anna, Cancel));
    }

    [Fact]
    public async Task AProductCreatedByHandHasNoPurchasesAndLaterReceiptsCanFindIt()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;

        var created = await CreateAsync(services, new CreateProductCommand("  Kefir   naturalny Bio ", "Bakoma", Milk, ProductOrigin.Barcode));

        Assert.Equal("Kefir naturalny Bio", created.CanonicalName);
        Assert.Equal(ProductOrigin.Barcode, created.Origin);
        Assert.False(created.NeedsReview);
        Assert.Equal((Milk, ProductAttributeSource.Confirmed), (created.CategoryId, created.CategorySource));
        Assert.Equal(("Bakoma", ProductAttributeSource.Confirmed), (created.Brand, created.BrandSource));
        await using (var scope = services.CreateAsyncScope())
        {
            var detail = await scope.ServiceProvider.GetRequiredService<IProductCatalogService>().GetProductAsync(created.Id, Cancel);
            Assert.Equal(0, detail!.PurchaseCount);
            Assert.Empty(detail.Aliases);
            var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            Assert.Contains(
                await dbContext.ProductAuditEntries.Where(entry => entry.ProductId == created.Id).ToListAsync(Cancel),
                entry => entry.FieldName == ProductAuditEntry.CreatedField && entry.NewValue == "barcode");
        }

        // No store alias is invented; similarity finds it for any store's identical wording.
        await SaveAsync(services, "Biedronka", ("KEFIR NATURALNY BIO", 4.99m));
        await using var check = services.CreateAsyncScope();
        var context = check.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        Assert.Equal(created.Id, (await context.Set<PurchaseItem>().SingleAsync(Cancel)).ProductId);

        // Exact namesakes are refused with the existing product's id; bad input is refused too.
        var duplicate = await Assert.ThrowsAsync<ProductConflictException>(
            () => CreateAsync(services, new CreateProductCommand("kefir NATURALNY bio")));
        Assert.Equal(created.Id, duplicate.ExistingProductId);
        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync(services, new CreateProductCommand(" ")));
        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync(services, new CreateProductCommand("%%%")));
        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync(services, new CreateProductCommand(new string('a', 501))));
        await Assert.ThrowsAsync<ArgumentException>(() => CreateAsync(services, new CreateProductCommand("Ser", Origin: ProductOrigin.Receipt)));
        await Assert.ThrowsAsync<ProductCategoryNotFoundException>(
            () => CreateAsync(services, new CreateProductCommand("Ser", CategoryId: Guid.NewGuid())));
    }

    [Fact]
    public async Task RenameChangesTheNameAndTheMatchingKeyAndIsAudited()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        var product = await CreateAsync(services, new CreateProductCommand("ACTIV KEFI NAT"));

        await using var scope = services.CreateAsyncScope();
        var management = scope.ServiceProvider.GetRequiredService<IProductManagementService>();
        var renamed = await management.RenameAsync(product.Id, "Kefir Activia naturalny 280 g", Anna, Cancel);

        Assert.Equal("Kefir Activia naturalny 280 g", renamed.CanonicalName);
        Assert.Equal("kefir activia naturalny 280 g", renamed.NormalizedName);
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var entry = await dbContext.ProductAuditEntries
            .SingleAsync(audit => audit.ProductId == product.Id && audit.FieldName == ProductAuditEntry.NameField, Cancel);
        Assert.Equal(("ACTIV KEFI NAT", "Kefir Activia naturalny 280 g"), (entry.OldValue, entry.NewValue));

        await Assert.ThrowsAsync<ProductNotFoundException>(() => management.RenameAsync(Guid.NewGuid(), "X", Anna, Cancel));
        await Assert.ThrowsAsync<ArgumentException>(() => management.RenameAsync(product.Id, "  ", Anna, Cancel));
    }

    [Fact]
    public async Task CategorizationOfAProductMergedAwayDoesNothing()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        var source = await CreateAsync(services, new CreateProductCommand("Kakao"));
        var target = await CreateAsync(services, new CreateProductCommand("Kakao DecoMorreno"));
        await MergeAsync(services, source.Id, target.Id);

        // The worker may still hold the merged-away id from the save that queued it.
        await using var scope = services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProductCategoryRepository>();
        Assert.Empty(await repository.ListUnclassifiedProductsAsync([source.Id], Cancel));
        var written = await repository.ApplySuggestionAsync(source.Id, Milk, "Brand", "brand", July1, Cancel);
        Assert.False(written.CategoryWritten);
        Assert.False(written.BrandWritten);
        await repository.SaveChangesAsync(Cancel);
    }

    [Fact]
    public async Task ClassifyingAProductMergedAwayMeanwhileIsNotFound()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        var source = await CreateAsync(services, new CreateProductCommand("Kakao"));
        var target = await CreateAsync(services, new CreateProductCommand("Kakao DecoMorreno"));

        await using var scope = services.CreateAsyncScope();
        // The other member merges the product between this request's read and its save.
        var repository = new BeforeSaveRepository(
            scope.ServiceProvider.GetRequiredService<IProductCategoryRepository>(),
            () => MergeAsync(services, source.Id, target.Id));
        var service = new ProductCategoryService(repository, scope.ServiceProvider.GetRequiredService<TimeProvider>());

        await Assert.ThrowsAsync<ProductNotFoundException>(() => service.ClassifyProductAsync(
            new ClassifyProductCommand(source.Id, true, Milk, false, null), Anna, Cancel));
    }

    [Fact]
    public async Task CatalogAndProductPageCarryTheFlagAndItsCandidates()
    {
        await using var application = await TestApplication.CreateAsync();
        var services = application.Services;
        await SaveAsync(services, "Biedronka", ("ACTIV KEFI BOR 280", 3.49m));
        await SaveAsync(services, "Biedronka", ("ACTIV KEFI NAT 280", 3.59m));
        var existing = await FindProductAsync(services, "ACTIV KEFI BOR 280");
        var flagged = await FindProductAsync(services, "ACTIV KEFI NAT 280");

        await using var scope = services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IProductCatalogService>();
        var page = await catalog.GetCatalogAsync(new ProductCatalogQuery { NeedsReview = true }, Cancel);
        Assert.Equal(flagged.Id, Assert.Single(page.Items).ProductId);
        Assert.True(page.Items[0].NeedsReview);
        var picker = await catalog.GetCatalogAsync(new ProductCatalogQuery { Query = "kefi", ExcludeProductId = flagged.Id }, Cancel);
        Assert.Equal(existing.Id, Assert.Single(picker.Items).ProductId);

        var detail = await catalog.GetProductAsync(flagged.Id, Cancel);
        Assert.True(detail!.NeedsReview);
        Assert.Equal(ProductReviewReason.AmbiguousMatch, detail.ReviewReason);
        var candidate = Assert.Single(detail.ReviewCandidates!);
        Assert.Equal((existing.Id, "ACTIV KEFI BOR 280", 1), (candidate.ProductId, candidate.Name, candidate.PurchaseCount));
        var plain = await catalog.GetProductAsync(existing.Id, Cancel);
        Assert.False(plain!.NeedsReview);
        Assert.Empty(plain.ReviewCandidates!);
    }

    public static TheoryData<string, string> ProtectedCalls => new()
    {
        { "POST", "/api/products" },
        { "PUT", $"/api/products/{Guid.NewGuid()}/name" },
        { "POST", $"/api/products/{Guid.NewGuid()}/merge-into/{Guid.NewGuid()}" },
        { "POST", $"/api/products/{Guid.NewGuid()}/confirm-distinct" },
        { "GET", "/api/products?needsReview=true" }
    };

    [Theory]
    [MemberData(nameof(ProtectedCalls))]
    public async Task EveryProductChangeNeedsAnApprovedValidToken(string method, string path)
    {
        await using var host = new AuthTestHost();
        foreach (var token in new[]
                 {
                     null,
                     AuthTestHost.Token(key: AuthTestHost.ForeignKey),
                     AuthTestHost.Token(expires: DateTime.UtcNow.AddMinutes(-5))
                 })
        {
            using var client = host.CreateClientFor(token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Request(method, path), Cancel)).StatusCode);
        }

        using var pending = host.CreateClientFor(AuthTestHost.Token(access: "pending"));
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.SendAsync(Request(method, path), Cancel)).StatusCode);
    }

    [Fact]
    public async Task HttpCreateRenameMergeAndConfirmDistinct()
    {
        await using var host = new AuthTestHost();
        await SaveAsync(host.Services, "Biedronka", ("ACTIV KEFI BOR 280", 3.49m));
        await SaveAsync(host.Services, "Biedronka", ("ACTIV KEFI NAT 280", 3.59m));
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/products?needsReview=true", Cancel);
        Assert.Equal(1, catalog.GetProperty("needsReviewCount").GetInt32());
        var flagged = Assert.Single(catalog.GetProperty("items").EnumerateArray());
        Assert.True(flagged.GetProperty("needsReview").GetBoolean());
        Assert.Equal("receipt", flagged.GetProperty("origin").GetString());
        var flaggedId = flagged.GetProperty("id").GetGuid();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/products/{flaggedId}", Cancel);
        Assert.Equal("ambiguousMatch", detail.GetProperty("reviewReason").GetString());
        var candidate = Assert.Single(detail.GetProperty("reviewCandidates").EnumerateArray());
        var existingId = candidate.GetProperty("productId").GetGuid();
        Assert.Equal("ACTIV KEFI BOR 280", candidate.GetProperty("name").GetString());

        var created = await client.PostAsJsonAsync("/api/products", new { name = "Kefir Bio", brand = "Bakoma", origin = "barcode" }, Cancel);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var product = await created.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("barcode", product.GetProperty("origin").GetString());
        var createdId = product.GetProperty("id").GetGuid();
        var duplicate = await client.PostAsJsonAsync("/api/products", new { name = "KEFIR bio" }, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var problem = await duplicate.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(createdId, problem.GetProperty("existingProductId").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/products", new { name = "" }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/products", new { name = "X", origin = "receipt" }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/products", new { name = "X", categoryId = Guid.NewGuid() }, Cancel)).StatusCode);

        var renamed = await client.PutAsJsonAsync($"/api/products/{createdId}/name", new { name = "Kefir Bakoma Bio" }, Cancel);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Kefir Bakoma Bio", (await renamed.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}/name", new { name = "X" }, Cancel)).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/api/products/{flaggedId}/merge-into/{flaggedId}", null, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/products/{flaggedId}/merge-into/{Guid.NewGuid()}", null, Cancel)).StatusCode);
        var merge = await client.PostAsync($"/api/products/{flaggedId}/merge-into/{existingId}", null, Cancel);
        Assert.Equal(HttpStatusCode.OK, merge.StatusCode);
        var merged = await merge.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(existingId, merged.GetProperty("targetProductId").GetGuid());
        Assert.Equal(1, merged.GetProperty("movedLineCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/products/{flaggedId}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/products/{flaggedId}/merge-into/{existingId}", null, Cancel)).StatusCode);

        var distinct = await client.PostAsync($"/api/products/{createdId}/confirm-distinct", null, Cancel);
        Assert.Equal(HttpStatusCode.OK, distinct.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/products/{Guid.NewGuid()}/confirm-distinct", null, Cancel)).StatusCode);

        var after = await client.GetFromJsonAsync<JsonElement>("/api/products", Cancel);
        Assert.Equal(0, after.GetProperty("needsReviewCount").GetInt32());
        var count = await client.GetFromJsonAsync<JsonElement>("/api/review-queue/count", Cancel);
        Assert.False(count.GetProperty("byKind").TryGetProperty("productNeedsReview", out _));
    }

    private static HttpRequestMessage Request(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(new { name = "Проба" });
        }

        return request;
    }

    private static async Task<Product> CreateAsync(IServiceProvider services, CreateProductCommand command)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IProductManagementService>().CreateAsync(command, Anna, Cancel);
    }

    private static async Task<ProductMergeResult> MergeAsync(IServiceProvider services, Guid sourceId, Guid targetId)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IProductManagementService>()
            .MergeAsync(sourceId, targetId, Anna, Cancel);
    }

    private static async Task<Product> FindProductAsync(IServiceProvider services, string name)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        return await dbContext.Products.AsNoTracking().SingleAsync(product => product.CanonicalName == name, Cancel);
    }

    private static async Task<Guid[]> CreateTagsAsync(IServiceProvider services, params string[] names)
    {
        await using var scope = services.CreateAsyncScope();
        var tags = scope.ServiceProvider.GetRequiredService<IProductTagService>();
        var ids = new List<Guid>();
        foreach (var name in names)
        {
            ids.Add((await tags.CreateAsync(name, Cancel)).Id);
        }

        return [.. ids];
    }

    private static async Task SetTagsAsync(IServiceProvider services, Guid productId, params Guid[] tagIds)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IProductTagService>().SetProductTagsAsync(productId, tagIds, Anna, Cancel);
    }

    private static async Task ClassifyAsync(IServiceProvider services, Guid productId, Guid categoryId, string brand)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IProductCategoryService>().ClassifyProductAsync(
            new ClassifyProductCommand(productId, true, categoryId, true, brand), Anna, Cancel);
    }

    private static async Task<Guid> AddImpressionAsync(IServiceProvider services, Guid productId)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var line = await dbContext.Set<PurchaseItem>()
            .Where(item => item.ProductId == productId)
            .Select(item => new { item.Id, item.Name, PurchaseId = EF.Property<Guid>(item, "PurchaseDraftId") })
            .FirstAsync(Cancel);
        var impression = ProductImpression.Create(
            Guid.NewGuid(), line.PurchaseId, line.Id, productId, line.Name, Anna.UserId, Anna.Email,
            ImpressionRating.Like, null, null, null, July1);
        dbContext.ProductImpressions.Add(impression);
        await dbContext.SaveChangesAsync(Cancel);
        return impression.Id;
    }

    private static async Task<int> CountLinesAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>().Set<PurchaseItem>().CountAsync(Cancel);
    }

    private static async Task<decimal?> SpendAsync(TestApplication application)
    {
        var answer = await AnalyticsExecutorTests.RunAsync(
            application,
            new AnalyticsPlan { From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 7, 13) });
        return answer.Result.Kpis.SingleOrDefault(kpi => kpi.Code == "spend")?.Value;
    }

    private static async Task<ReviewQueueResult> QueueAsync(AsyncServiceScope scope) =>
        await scope.ServiceProvider.GetRequiredService<IReviewQueueService>().GetAsync(20, Cancel);

    private static ReviewQueueGroup? Find(ReviewQueueResult queue, ReviewQueueItemKind kind) =>
        queue.Groups.SingleOrDefault(group => group.Kind == kind);

    private static async Task SaveAsync(IServiceProvider services, string merchant, params (string Name, decimal Price)[] lines)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>().CreateAsync(
            new CreatePurchaseDraftCommand(
                merchant,
                null,
                "PLN",
                null,
                lines.Select(line => new PurchaseItemCommand(line.Name, 1, "szt", line.Price, line.Price)).ToList(),
                AllowPossibleDuplicate: true),
            Cancel);
    }

    /// <summary>The real repository, with something happening right before the save.</summary>
    private sealed class BeforeSaveRepository(IProductCategoryRepository inner, Func<Task> beforeSave) : IProductCategoryRepository
    {
        public Task<IReadOnlyList<ProductCategory>> ListCategoriesAsync(CancellationToken cancellationToken) => inner.ListCategoriesAsync(cancellationToken);

        public Task<IReadOnlyDictionary<Guid, int>> CountProductsByCategoryAsync(CancellationToken cancellationToken) => inner.CountProductsByCategoryAsync(cancellationToken);

        public void AddCategory(ProductCategory category) => inner.AddCategory(category);

        public void RemoveCategory(ProductCategory category) => inner.RemoveCategory(category);

        public Task<IReadOnlyList<Product>> ListProductsInCategoryAsync(Guid categoryId, CancellationToken cancellationToken) =>
            inner.ListProductsInCategoryAsync(categoryId, cancellationToken);

        public Task<Product?> FindProductForUpdateAsync(Guid productId, CancellationToken cancellationToken) =>
            inner.FindProductForUpdateAsync(productId, cancellationToken);

        public Task<IReadOnlyList<Product>> ListUnclassifiedProductsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken cancellationToken) =>
            inner.ListUnclassifiedProductsAsync(productIds, cancellationToken);

        public Task<IReadOnlyList<Product>> ListUnclassifiedProductsAsync(int maximumProducts, bool includeAttempted, CancellationToken cancellationToken) =>
            inner.ListUnclassifiedProductsAsync(maximumProducts, includeAttempted, cancellationToken);

        public Task<SuggestionWrite> ApplySuggestionAsync(Guid productId, Guid? categoryId, string? brand, string? normalizedBrand, DateTimeOffset now, CancellationToken cancellationToken) =>
            inner.ApplySuggestionAsync(productId, categoryId, brand, normalizedBrand, now, cancellationToken);

        public void AddAuditEntry(ProductAuditEntry entry) => inner.AddAuditEntry(entry);

        public Task<IReadOnlyList<ProductAuditEntry>> ListAuditEntriesAsync(Guid productId, CancellationToken cancellationToken) =>
            inner.ListAuditEntriesAsync(productId, cancellationToken);

        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            await beforeSave();
            await inner.SaveChangesAsync(cancellationToken);
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Application.ReviewQueue;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Human product names (Polish + Russian, stage 1.4): suggested in the same
/// categorizer call, never over a person's name, searchable, editable,
/// confirmable, re-suggestable, carried on purchase lines and in the «Ревью»
/// hub. Runs on SQLite and, opted in, Postgres.
/// </summary>
public sealed class ProductNameTests
{
    private const string KefirPl = "Kefir naturalny Activia 280 g";
    private const string KefirRu = "Кефир натуральный Activia, 280 г";
    private static readonly ReceiptActor Anna = new("user-anna", "anna@example.com");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheCategorizerCallSuggestsBothNamesAndSearchFindsThemByPolishRussianAndReceiptText()
    {
        await using var application = await TestApplication.CreateAsync();
        var kefir = await SaveProductAsync(application, "ACTIV KEFI NAT 280");
        var bread = await SaveProductAsync(application, "CHLEB ZYTNI");
        var calls = 0;
        var categorizer = new FakeCategorizer(products =>
        {
            calls++;
            return products.Select(product => product.ProductId == kefir
                    ? new ProductCategorySuggestion(product.ProductId, null, "Activia", KefirPl, KefirRu)
                    : new ProductCategorySuggestion(product.ProductId, null, null))
                .ToList();
        });

        var summary = await RunAfterSaveAsync(application, categorizer, [kefir, bread]);

        // One request for the batch: names ride on the category call.
        Assert.Equal(1, calls);
        Assert.Equal((2, 1, 1), (summary.Considered, summary.Suggested, summary.NamesSuggested));
        var named = await ReadProductAsync(application, kefir);
        Assert.Equal((KefirPl, ProductAttributeSource.Suggested), (named.NamePl, named.NamePlSource));
        Assert.Equal((KefirRu, ProductAttributeSource.Suggested), (named.NameRu, named.NameRuSource));
        Assert.Equal("ACTIV KEFI NAT 280", named.CanonicalName);
        var unnamed = await ReadProductAsync(application, bread);
        Assert.Null(unnamed.NamePl);
        Assert.Null(unnamed.NamePlSource);
        Assert.NotNull(unnamed.NamingAttemptedAt);

        foreach (var query in new[] { "kefir", "КЕФИР", "кефир натуральный", "kefi", "naturalny activia", "ACTIV KEFI" })
        {
            var page = await SearchAsync(application, query);
            var entry = Assert.Single(page.Items);
            Assert.Equal(kefir, entry.ProductId);
            Assert.Equal(KefirPl, entry.Names?.NamePl);
            Assert.Equal(ProductAttributeSource.Suggested, entry.Names?.NameRuSource);
        }

        // The answer counts as an attempt: the next save does not pay again.
        Assert.Equal(0, (await RunAfterSaveAsync(application, categorizer, [kefir, bread])).Considered);
        Assert.Equal(1, calls);

        await using var scope = application.Services.CreateAsyncScope();
        var audit = await scope.ServiceProvider.GetRequiredService<IProductCategoryService>().GetAuditTrailAsync(kefir, Cancel);
        Assert.Contains(audit, entry => entry.FieldName == ProductAuditEntry.NamePlField
            && entry.NewValue == $"{KefirPl} (suggested)" && entry.Actor == "ai:fake-model");
        Assert.Contains(audit, entry => entry.FieldName == ProductAuditEntry.NameRuField);
    }

    [Fact]
    public async Task AConfirmedNameIsNeverOverwrittenByAfterSaveBackfillOrSuggestAgain()
    {
        await using var application = await TestApplication.CreateAsync();
        var kefir = await SaveProductAsync(application, "ACTIV KEFI NAT 280");
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var names = await scope.ServiceProvider.GetRequiredService<IProductCategoryService>()
                .SetProductNameAsync(kefir, ProductNameLanguage.Polish, "  Kefir   Activia ", Anna, Cancel);
            Assert.Equal(("Kefir Activia", ProductAttributeSource.Confirmed), (names.NamePl, names.NamePlSource));
        }

        var categorizer = new FakeCategorizer(products => products
            .Select(product => new ProductCategorySuggestion(product.ProductId, null, null, "Jogurt", "Йогурт"))
            .ToList());

        await RunAfterSaveAsync(application, categorizer, [kefir]);
        await RunBackfillAsync(application, categorizer, retryAttempted: true);
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var again = await CreateService(scope, categorizer).SuggestNamesAsync(kefir, Cancel);
            Assert.Equal(("Kefir Activia", ProductAttributeSource.Confirmed), (again.NamePl, again.NamePlSource));
            Assert.Equal(("Йогурт", ProductAttributeSource.Suggested), (again.NameRu, again.NameRuSource));
        }

        var product = await ReadProductAsync(application, kefir);
        Assert.Equal(("Kefir Activia", ProductAttributeSource.Confirmed), (product.NamePl, product.NamePlSource));
        Assert.Equal("kefir activia", product.NormalizedNamePl);

        // A confirmed empty name («show the receipt text») is a decision too.
        await using (var scope = application.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IProductCategoryService>()
                .SetProductNameAsync(kefir, ProductNameLanguage.Russian, "   ", Anna, Cancel);
            await CreateService(scope, categorizer).SuggestNamesAsync(kefir, Cancel);
        }

        var cleared = await ReadProductAsync(application, kefir);
        Assert.Equal((null, ProductAttributeSource.Confirmed), (cleared.NameRu, cleared.NameRuSource));
        Assert.Null(cleared.NormalizedNameRu);
    }

    [Fact]
    public async Task BackfillFillsOnlyMissingNamesAndSuggestAgainReplacesSuggestions()
    {
        await using var application = await TestApplication.CreateAsync();
        var kefir = await SaveProductAsync(application, "ACTIV KEFI NAT 280");
        var bread = await SaveProductAsync(application, "CHLEB ZYTNI");
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IProductCategoryRepository>();
            // As before this stage: classified products, no names yet; the kefir already has a Polish suggestion.
            foreach (var id in new[] { kefir, bread })
            {
                await repository.ApplySuggestionAsync(id, ProductCategorySeed.IdFor(2), null, null, DateTimeOffset.UnixEpoch, Cancel);
            }

            await repository.ApplyNameSuggestionAsync(kefir, ProductNameSuggestion.From(KefirPl, null), false, DateTimeOffset.UnixEpoch, Cancel);
            await scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>().Products
                .Where(product => product.Id == kefir)
                .ExecuteUpdateAsync(update => update.SetProperty(product => product.NamingAttemptedAt, (DateTimeOffset?)null), Cancel);
        }

        var sent = new List<string>();
        var categorizer = new FakeCategorizer(products =>
        {
            sent.AddRange(products.Select(product => product.Name));
            return products.Select(product => new ProductCategorySuggestion(
                product.ProductId, ProductCategorySeed.IdFor(3), "X", $"{product.Name} pl", $"{product.Name} ru")).ToList();
        });

        var summary = await RunBackfillAsync(application, categorizer);

        Assert.Equal(["ACTIV KEFI NAT 280", "CHLEB ZYTNI"], sent.Order(StringComparer.Ordinal));
        Assert.Equal(2, summary.NamesSuggested);
        var filled = await ReadProductAsync(application, kefir);
        // The existing Polish suggestion stays; only the missing Russian one is filled.
        Assert.Equal(KefirPl, filled.NamePl);
        Assert.Equal("ACTIV KEFI NAT 280 ru", filled.NameRu);
        // Named-only products keep their category: the backfill came for the names.
        Assert.Equal((ProductCategorySeed.IdFor(2), ProductAttributeSource.Suggested), (filled.CategoryId, filled.CategorySource));
        Assert.Null(filled.Brand);

        // Everything named: the next backfill (even a retry) sends nothing for names.
        sent.Clear();
        Assert.Equal(0, (await RunBackfillAsync(application, categorizer, retryAttempted: true)).Considered);
        Assert.Empty(sent);

        // «Предложить заново» replaces a suggestion.
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var again = await CreateService(scope, categorizer).SuggestNamesAsync(kefir, Cancel);
            Assert.Equal("ACTIV KEFI NAT 280 pl", again.NamePl);
        }
    }

    [Fact]
    public async Task SuggestAgainFailsCleanlyWithoutAModelOrWhenTheModelFails()
    {
        await using var application = await TestApplication.CreateAsync();
        var kefir = await SaveProductAsync(application, "ACTIV KEFI NAT 280");
        await using var scope = application.Services.CreateAsyncScope();

        var unconfigured = new FakeCategorizer(_ => throw new InvalidOperationException("must not be called")) { IsConfigured = false };
        await Assert.ThrowsAsync<ProductNamingUnavailableException>(() => CreateService(scope, unconfigured).SuggestNamesAsync(kefir, Cancel));

        var failing = new FakeCategorizer(_ => throw new ProductCategorizationException("model down"));
        await Assert.ThrowsAsync<ProductNamingUnavailableException>(() => CreateService(scope, failing).SuggestNamesAsync(kefir, Cancel));

        await Assert.ThrowsAsync<ProductNotFoundException>(() => CreateService(scope, failing).SuggestNamesAsync(Guid.NewGuid(), Cancel));
        Assert.Null((await ReadProductAsync(application, kefir)).NamePl);
    }

    [Fact]
    public async Task ConfirmSetAndMergeKeepTheAuditAndThePersonsNames()
    {
        await using var application = await TestApplication.CreateAsync();
        var kefir = await SaveProductAsync(application, "ACTIV KEFI NAT 280");
        var duplicate = await SaveProductAsync(application, "KEFIR ACTIVIA");
        await SuggestNamesAsync(application, kefir, KefirPl, KefirRu);

        await using (var scope = application.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();
            var confirmed = await service.ConfirmSuggestedNamesAsync(kefir, null, Anna, Cancel);
            Assert.Equal((ProductAttributeSource.Confirmed, ProductAttributeSource.Confirmed), (confirmed.NamePlSource, confirmed.NameRuSource));
            // Nothing suggested any more: confirming again changes nothing.
            await service.ConfirmSuggestedNamesAsync(kefir, null, Anna, Cancel);

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.SetProductNameAsync(kefir, ProductNameLanguage.Polish, new string('x', Product.MaximumDisplayNameLength + 1), Anna, Cancel));
            await Assert.ThrowsAsync<ProductNotFoundException>(() =>
                service.SetProductNameAsync(Guid.NewGuid(), ProductNameLanguage.Polish, "X", Anna, Cancel));

            var audit = await service.GetAuditTrailAsync(kefir, Cancel);
            var confirmations = audit.Where(entry => entry.Source == ProductAuditChangeSource.Human).ToList();
            Assert.Equal(2, confirmations.Count);
            Assert.Contains(confirmations, entry => entry.FieldName == ProductAuditEntry.NamePlField
                && entry.OldValue == $"{KefirPl} (suggested)" && entry.NewValue == KefirPl && entry.Actor == Anna.UserId);

            await service.SetProductNameAsync(duplicate, ProductNameLanguage.Russian, "Кефир", Anna, Cancel);
        }

        // Merging the duplicate in: the target's confirmed names win; its own Russian stays.
        await using (var scope = application.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IProductManagementService>().MergeAsync(duplicate, kefir, Anna, Cancel);
        }

        var merged = await ReadProductAsync(application, kefir);
        Assert.Equal((KefirPl, KefirRu), (merged.NamePl, merged.NameRu));

        // The other way round a confirmed name moves over to an unnamed target.
        var bread = await SaveProductAsync(application, "CHLEB");
        var bread2 = await SaveProductAsync(application, "CHLEB ZYTNI");
        await using (var scope = application.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IProductCategoryService>()
                .SetProductNameAsync(bread2, ProductNameLanguage.Polish, "Chleb żytni", Anna, Cancel);
            await scope.ServiceProvider.GetRequiredService<IProductManagementService>().MergeAsync(bread2, bread, Anna, Cancel);
        }

        var target = await ReadProductAsync(application, bread);
        Assert.Equal(("Chleb żytni", ProductAttributeSource.Confirmed, "chleb zytni"), (target.NamePl, target.NamePlSource, target.NormalizedNamePl));
    }

    [Fact]
    public async Task VernoConfirmsOnlyTheNamesThePersonSaw()
    {
        await using var application = await TestApplication.CreateAsync();
        var kefir = await SaveProductAsync(application, "ACTIV KEFI NAT 280");
        await SuggestNamesAsync(application, kefir, KefirPl, KefirRu);

        await using (var scope = application.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IProductCategoryService>();
            // A new suggestion arrived after the screen loaded: nothing is confirmed.
            await Assert.ThrowsAsync<ProductCategoryConflictException>(() =>
                service.ConfirmSuggestedNamesAsync(kefir, new ShownProductNames("Kefir", KefirRu), Anna, Cancel));
        }

        var untouched = await ReadProductAsync(application, kefir);
        Assert.Equal((ProductAttributeSource.Suggested, ProductAttributeSource.Suggested), (untouched.NamePlSource, untouched.NameRuSource));

        await using (var scope = application.Services.CreateAsyncScope())
        {
            var confirmed = await scope.ServiceProvider.GetRequiredService<IProductCategoryService>()
                .ConfirmSuggestedNamesAsync(kefir, new ShownProductNames($" {KefirPl} ", KefirRu), Anna, Cancel);
            Assert.Equal((ProductAttributeSource.Confirmed, ProductAttributeSource.Confirmed), (confirmed.NamePlSource, confirmed.NameRuSource));
        }
    }

    [Fact]
    public async Task APersonsNameInOneLanguageIsSentAsAHintForTheOther()
    {
        await using var application = await TestApplication.CreateAsync();
        var kefir = await SaveProductAsync(application, "ACTIV KEFI NAT 280");
        await using (var scope = application.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IProductCategoryService>()
                .SetProductNameAsync(kefir, ProductNameLanguage.Polish, KefirPl, Anna, Cancel);
        }

        ProductToCategorize? sent = null;
        var categorizer = new FakeCategorizer(products =>
        {
            sent = products.Single();
            return [new ProductCategorySuggestion(sent.ProductId, null, null, "Kefir", KefirRu)];
        });
        await RunAfterSaveAsync(application, categorizer, [kefir]);

        Assert.Equal((KefirPl, (string?)null), (sent!.ConfirmedNamePl, sent.ConfirmedNameRu));
        var product = await ReadProductAsync(application, kefir);
        Assert.Equal((KefirPl, KefirRu), (product.NamePl, product.NameRu));
    }

    [Fact]
    public async Task PurchaseLinesCarryTheProductsPolishName()
    {
        await using var host = new AuthTestHost();
        Guid purchaseId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var view = await scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>().CreateAsync(
                new CreatePurchaseDraftCommand(
                    "Biedronka", null, "PLN", null,
                    [new PurchaseItemCommand("ACTIV KEFI NAT 280", 1, "szt", 3.49m, 3.49m), new PurchaseItemCommand("CHLEB", 1, "szt", 4m, 4m)],
                    AllowPossibleDuplicate: true),
                Cancel);
            purchaseId = view.PurchaseId;
            var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            var kefir = (await context.Products.AsNoTracking().SingleAsync(product => product.CanonicalName == "ACTIV KEFI NAT 280", Cancel)).Id;
            await scope.ServiceProvider.GetRequiredService<IProductCategoryRepository>().ApplyNameSuggestionAsync(
                kefir, ProductNameSuggestion.From(KefirPl, KefirRu), false, DateTimeOffset.UtcNow, Cancel);
        }

        using var client = host.CreateClientFor(AuthTestHost.Token());
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/purchases/{purchaseId}", Cancel);
        var items = detail.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(("ACTIV KEFI NAT 280", KefirPl), (items[0].GetProperty("name").GetString(), items[0].GetProperty("productNamePl").GetString()));
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("productNamePl").ValueKind);
    }

    [Fact]
    public async Task TheHubListsSuggestedNamesInTheirOwnSectionUntilConfirmed()
    {
        await using var application = await TestApplication.CreateAsync();
        var kefir = await SaveProductAsync(application, "ACTIV KEFI NAT 280");
        var bread = await SaveProductAsync(application, "CHLEB");
        await SuggestNamesAsync(application, kefir, KefirPl, KefirRu);
        await SuggestNamesAsync(application, bread, "Chleb", null);

        var hub = await HubAsync(application);
        var names = Assert.Single(hub.Groups, group => group.Kind == ReviewQueueItemKind.SuggestedNames);
        Assert.Equal(2, names.TotalCount);
        var kefirItem = Assert.Single(names.Items, item => item.ProductId == kefir);
        Assert.Equal(("ACTIV KEFI NAT 280", KefirPl, KefirRu), (kefirItem.Title, kefirItem.NamePl, kefirItem.NameRu));
        Assert.True(kefirItem.NamePlSuggested && kefirItem.NameRuSuggested);
        var breadItem = Assert.Single(names.Items, item => item.ProductId == bread);
        Assert.Equal((true, false), (breadItem.NamePlSuggested, breadItem.NameRuSuggested));
        Assert.Equal(2, hub.Summary!.Sections.Single(section => section.Section == ReviewQueueSection.Names).Count);
        Assert.Equal(hub.Summary.Sections.Sum(section => section.Count), hub.Summary.TotalCount);
        // The category items show the human name too.
        Assert.Contains(hub.Groups.SelectMany(group => group.Items), item => item.Kind == ReviewQueueItemKind.MissingCategory && item.NamePl == KefirPl);

        await using (var scope = application.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IProductCategoryService>().ConfirmSuggestedNamesAsync(kefir, null, Anna, Cancel);
        }

        var after = await HubAsync(application);
        var remaining = Assert.Single(after.Groups, group => group.Kind == ReviewQueueItemKind.SuggestedNames);
        Assert.Equal(bread, Assert.Single(remaining.Items).ProductId);
        Assert.Equal(1, after.Summary!.Sections.Single(section => section.Section == ReviewQueueSection.Names).Count);

        // The data queue (MCP, badge before the hub) has no names kind.
        await using (var scope = application.Services.CreateAsyncScope())
        {
            var queue = await scope.ServiceProvider.GetRequiredService<IReviewQueueService>().GetAsync(20, Cancel);
            Assert.DoesNotContain(queue.Groups, group => group.Kind == ReviewQueueItemKind.SuggestedNames);
        }
    }

    public static TheoryData<string, string> ProtectedCalls => new()
    {
        { "PUT", $"/api/products/{Guid.NewGuid()}/name" },
        { "POST", $"/api/products/{Guid.NewGuid()}/names/confirm" },
        { "POST", $"/api/products/{Guid.NewGuid()}/name-suggestions" }
    };

    [Theory]
    [MemberData(nameof(ProtectedCalls))]
    public async Task EveryNameCallNeedsAnApprovedValidToken(string method, string path)
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
    public async Task TheApiEditsConfirmsAndShowsNamesEverywhere()
    {
        await using var host = new AuthTestHost();
        Guid kefir;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>().CreateAsync(
                new CreatePurchaseDraftCommand(
                    "Biedronka", null, "PLN", null,
                    [new PurchaseItemCommand("ACTIV KEFI NAT 280", 1, "szt", 3.49m, 3.49m)],
                    AllowPossibleDuplicate: true),
                Cancel);
            var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
            kefir = (await context.Products.AsNoTracking().SingleAsync(Cancel)).Id;
            await scope.ServiceProvider.GetRequiredService<IProductCategoryRepository>().ApplyNameSuggestionAsync(
                kefir, ProductNameSuggestion.From(KefirPl, KefirRu), false, DateTimeOffset.UtcNow, Cancel);
        }

        using var client = host.CreateClientFor(AuthTestHost.Token());

        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/products?q=%D0%BA%D0%B5%D1%84%D0%B8%D1%80", Cancel);
        var row = Assert.Single(catalog.GetProperty("items").EnumerateArray());
        Assert.Equal("ACTIV KEFI NAT 280", row.GetProperty("name").GetString());
        Assert.Equal(KefirPl, row.GetProperty("namePl").GetString());
        Assert.Equal("suggested", row.GetProperty("namePlSource").GetString());
        Assert.Equal(KefirRu, row.GetProperty("nameRu").GetString());

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/products/{kefir}", Cancel);
        Assert.Equal(KefirRu, detail.GetProperty("nameRu").GetString());
        Assert.Equal("suggested", detail.GetProperty("nameRuSource").GetString());

        var edited = await client.PutAsJsonAsync($"/api/products/{kefir}/name", new { name = "Kefir Activia", language = "pl" }, Cancel);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var editedBody = await edited.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(("Kefir Activia", "confirmed"), (editedBody.GetProperty("namePl").GetString(), editedBody.GetProperty("namePlSource").GetString()));
        Assert.Equal("suggested", editedBody.GetProperty("nameRuSource").GetString());

        // The Russian suggestion on screen is not what the product has: 409, nothing changes.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(
            $"/api/products/{kefir}/names/confirm", new { namePl = "Kefir Activia", nameRu = "Кефир" }, Cancel)).StatusCode);
        var confirmed = await client.PostAsJsonAsync(
            $"/api/products/{kefir}/names/confirm", new { namePl = "Kefir Activia", nameRu = KefirRu }, Cancel);
        var confirmedBody = await confirmed.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("confirmed", confirmedBody.GetProperty("nameRuSource").GetString());

        // Without a language the old rename of the receipt-text name still works.
        var renamed = await client.PutAsJsonAsync($"/api/products/{kefir}/name", new { name = "ACTIVIA KEFIR" }, Cancel);
        var renamedBody = await renamed.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(("ACTIVIA KEFIR", "Kefir Activia"), (renamedBody.GetProperty("name").GetString(), renamedBody.GetProperty("namePl").GetString()));

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/api/products/{kefir}/name", new { name = "X", language = "de" }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync($"/api/products/{kefir}/name", new { name = new string('x', 201), language = "ru" }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}/name", new { name = "X", language = "ru" }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsync($"/api/products/{Guid.NewGuid()}/names/confirm", null, Cancel)).StatusCode);

        // Both names decided by a person: nothing to ask the model, the names come back as they are.
        var settled = await client.PostAsync($"/api/products/{kefir}/name-suggestions", null, Cancel);
        Assert.Equal(HttpStatusCode.OK, settled.StatusCode);
        Assert.Equal("Kefir Activia", (await settled.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("namePl").GetString());

        // Tests never call a model: suggestions are switched off, so the endpoint says so.
        var created = await client.PostAsJsonAsync("/api/products", new { name = "Mleko" }, Cancel);
        var mleko = (await created.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
        var suggest = await client.PostAsync($"/api/products/{mleko}/name-suggestions", null, Cancel);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, suggest.StatusCode);
        var problem = await suggest.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("nameSuggestionsUnavailable", problem.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsync($"/api/products/{Guid.NewGuid()}/name-suggestions", null, Cancel)).StatusCode);
    }

    [Fact]
    public async Task SuggestAgainOverHttpReplacesOnlyWhatIsNotConfirmed()
    {
        var fake = new FakeCategorizer(products => products
            .Select(product => new ProductCategorySuggestion(product.ProductId, null, null, "Kefir", "Кефир"))
            .ToList());
        await using var host = new AuthTestHost(configureServices: services =>
            services.AddSingleton<IProductCategorizer>(fake));
        Guid kefir;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>().CreateAsync(
                new CreatePurchaseDraftCommand(
                    "Biedronka", null, "PLN", null,
                    [new PurchaseItemCommand("ACTIV KEFI NAT 280", 1, "szt", 3.49m, 3.49m)],
                    AllowPossibleDuplicate: true),
                Cancel);
            kefir = (await scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>().Products.AsNoTracking().SingleAsync(Cancel)).Id;
        }

        using var client = host.CreateClientFor(AuthTestHost.Token());
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/api/products/{kefir}/name", new { name = KefirPl, language = "pl" }, Cancel)).StatusCode);

        var suggest = await client.PostAsync($"/api/products/{kefir}/name-suggestions", null, Cancel);
        Assert.Equal(HttpStatusCode.OK, suggest.StatusCode);
        var body = await suggest.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal((KefirPl, "confirmed"), (body.GetProperty("namePl").GetString(), body.GetProperty("namePlSource").GetString()));
        Assert.Equal(("Кефир", "suggested"), (body.GetProperty("nameRu").GetString(), body.GetProperty("nameRuSource").GetString()));
    }

    private static HttpRequestMessage Request(string method, string path) =>
        new(new HttpMethod(method), path)
        {
            Content = method == "GET" ? null : JsonContent.Create(new { name = "X", language = "pl" })
        };

    private static async Task<ReviewQueueResult> HubAsync(TestApplication application)
    {
        await using var scope = application.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IReviewQueueService>()
            .GetHubAsync(20, ReviewQueueFilter.None, "user-anna", Cancel);
    }

    private static async Task<ProductCatalogPage> SearchAsync(TestApplication application, string query)
    {
        await using var scope = application.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IProductCatalogService>()
            .GetCatalogAsync(new ProductCatalogQuery { Query = query }, Cancel);
    }

    private static async Task SuggestNamesAsync(TestApplication application, Guid productId, string? namePl, string? nameRu)
    {
        await using var scope = application.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IProductCategoryRepository>().ApplyNameSuggestionAsync(
            productId, ProductNameSuggestion.From(namePl, nameRu), false, DateTimeOffset.UtcNow, Cancel);
    }

    private static async Task<Guid> SaveProductAsync(TestApplication application, string itemName)
    {
        await using (var scope = application.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPurchaseDraftService>().CreateAsync(
                new CreatePurchaseDraftCommand(
                    "Biedronka", null, "PLN", null,
                    [new PurchaseItemCommand(itemName, 1, "szt", 9.99m, 9.99m)],
                    AllowPossibleDuplicate: true),
                Cancel);
        }

        return await FindProductAsync(application, itemName);
    }

    private static async Task<Guid> FindProductAsync(TestApplication application, string itemName)
    {
        await using var scope = application.Services.CreateAsyncScope();
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

    private static async Task<ProductCategorizationRunSummary> RunAfterSaveAsync(
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

    private sealed class FakeCategorizer(Func<IReadOnlyList<ProductToCategorize>, IReadOnlyList<ProductCategorySuggestion>> respond)
        : IProductCategorizer
    {
        public bool IsConfigured { get; init; } = true;

        public string ModelName => "fake-model";

        public int MaximumBatchSize => 40;

        public Task<ProductCategorizationResult> SuggestAsync(
            IReadOnlyList<ProductToCategorize> products,
            IReadOnlyList<CategoryOption> categories,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ProductCategorizationResult(respond(products), 2, 3));
    }
}

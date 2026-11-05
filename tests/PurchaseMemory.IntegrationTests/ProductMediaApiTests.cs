using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Application.Products.Media;
using PurchaseMemory.Domain.Products;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Product photos, barcodes, «Больше не покупать», remembered codes and the
/// Open Food Facts proxy over HTTP: storage, the one-code-one-product rule,
/// idempotent device uploads, merge, audit, and real access-bypass attempts
/// (no token, a foreign signature, a not-approved user, ids of other products,
/// oversized and non-image uploads).
/// </summary>
public sealed class ProductMediaApiTests
{
    private const string AnnaId = "11111111-1111-1111-1111-111111111111";
    private const string BorisId = "22222222-2222-2222-2222-222222222222";

    // Real EAN-13 / EAN-8 / UPC-A codes with correct check digits.
    private const string Kefir = "5900512320045";
    private const string Milk = "5901234123457";
    private const string Ean8 = "96385074";
    private const string UpcA = "036000291452";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static string Anna => AuthTestHost.Token(subject: AnnaId, email: "anna@example.com");

    private static string Boris => AuthTestHost.Token(subject: BorisId, email: "boris@example.com");

    private static bool LocalStorage => Environment.GetEnvironmentVariable(ExternalTestServices.S3UrlVariable) is null or "";

    [Fact]
    public async Task PhotosAreStoredAsSmallMetadataFreeCopiesAndSharedInTheHousehold()
    {
        await using var host = new AuthTestHost();
        using var anna = host.CreateClientFor(Anna);
        using var boris = host.CreateClientFor(Boris);
        var productId = await CreateProductAsync(anna, "Kefir");

        var original = TestImages.WithExif(TestImages.Jpeg(3200, 1600), orientation: 1);
        var first = await UploadAsync(anna, productId, original);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.True(firstBody.GetProperty("created").GetBoolean());
        Assert.True(firstBody.GetProperty("firstPhoto").GetBoolean());
        var firstPhoto = firstBody.GetProperty("photo");
        Assert.True(firstPhoto.GetProperty("isMain").GetBoolean());
        Assert.Equal(1280, firstPhoto.GetProperty("width").GetInt32());
        var firstId = firstPhoto.GetProperty("id").GetGuid();

        // Served back to the other household member, through the API only.
        var served = await boris.GetAsync(firstPhoto.GetProperty("url").GetString(), Cancel);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/jpeg", served.Content.Headers.ContentType?.MediaType);
        Assert.Contains("private", served.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        var bytes = await served.Content.ReadAsByteArrayAsync(Cancel);
        using (var decoded = TestImages.Decode(bytes))
        {
            Assert.Equal(1280, Math.Max(decoded.Width, decoded.Height));
        }

        Assert.False(TestImages.ContainsSequence(bytes, "Exif\0\0"u8));
        Assert.False(TestImages.ContainsSequence(bytes, "SecretCam"u8));

        var thumbnail = await boris.GetByteArrayAsync(firstPhoto.GetProperty("thumbnailUrl").GetString(), Cancel);
        using (var decoded = TestImages.Decode(thumbnail))
        {
            Assert.Equal(256, Math.Max(decoded.Width, decoded.Height));
        }

        Assert.True(thumbnail.Length < 30 * 1024);

        // A later photo is not the first and not main; Boris makes it main.
        var second = await (await UploadAsync(boris, productId, TestImages.Png(600, 800))).Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.False(second.GetProperty("firstPhoto").GetBoolean());
        Assert.False(second.GetProperty("photo").GetProperty("isMain").GetBoolean());
        var secondId = second.GetProperty("photo").GetProperty("id").GetGuid();
        var afterMain = await (await boris.PutAsync($"/api/products/{productId}/photos/{secondId}/main", null, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(secondId, afterMain.GetProperty("mainPhotoId").GetGuid());
        Assert.Equal([false, true], afterMain.GetProperty("photos").EnumerateArray().Select(photo => photo.GetProperty("isMain").GetBoolean()));

        // The catalog row and the product page carry the main photo for thumbnails.
        var catalog = await anna.GetFromJsonAsync<JsonElement>("/api/products?q=kefir", Cancel);
        Assert.Equal(secondId, catalog.GetProperty("items")[0].GetProperty("mainPhotoId").GetGuid());
        var detail = await anna.GetFromJsonAsync<JsonElement>($"/api/products/{productId}", Cancel);
        Assert.Equal(secondId, detail.GetProperty("mainPhotoId").GetGuid());

        if (LocalStorage)
        {
            var folder = Path.Combine(host.DataDirectory, "receipts", "products", secondId.ToString("N"));
            Assert.Equal(["photo.jpg", "thumb.jpg"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
        }

        // Deleting the main photo makes the oldest remaining main and removes the files.
        Assert.Equal(HttpStatusCode.NoContent, (await anna.DeleteAsync($"/api/products/{productId}/photos/{secondId}", Cancel)).StatusCode);
        var media = await anna.GetFromJsonAsync<JsonElement>($"/api/products/{productId}/media", Cancel);
        Assert.Equal(firstId, media.GetProperty("mainPhotoId").GetGuid());
        Assert.Single(media.GetProperty("photos").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await anna.GetAsync($"/api/product-photos/{secondId}", Cancel)).StatusCode);
        if (LocalStorage)
        {
            Assert.False(Directory.Exists(Path.Combine(host.DataDirectory, "receipts", "products", secondId.ToString("N"))));
        }

        var audit = await AuditFieldsAsync(host, productId);
        Assert.Equal(3, audit.Count(entry => entry.Field == ProductAuditEntry.PhotoField));
        Assert.Contains(audit, entry => entry.Field == ProductAuditEntry.MainPhotoField && entry.Actor == BorisId);
    }

    [Fact]
    public async Task ARepeatedDeviceUploadReturnsTheStoredPhoto()
    {
        await using var host = new AuthTestHost();
        using var anna = host.CreateClientFor(Anna);
        var productId = await CreateProductAsync(anna, "Kefir");
        var otherId = await CreateProductAsync(anna, "Mleko");
        var photo = TestImages.Jpeg(400, 300);

        var first = await UploadAsync(anna, productId, photo, clientId: "device-photo-1");
        var again = await UploadAsync(anna, productId, photo, clientId: "device-photo-1");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var againBody = await again.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.False(againBody.GetProperty("created").GetBoolean());
        Assert.False(againBody.GetProperty("firstPhoto").GetBoolean());
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("photo").GetProperty("id").GetGuid(),
            againBody.GetProperty("photo").GetProperty("id").GetGuid());
        Assert.Single((await anna.GetFromJsonAsync<JsonElement>($"/api/products/{productId}/media", Cancel)).GetProperty("photos").EnumerateArray());

        // The client id is per product: the same id on another product is another photo.
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(anna, otherId, photo, clientId: "device-photo-1")).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await UploadAsync(anna, productId, photo, clientId: new string('x', 65))).StatusCode);
    }

    [Fact]
    public async Task OversizedEmptyAndNonImageUploadsAreRefusedWithoutStoringAnything()
    {
        await using var host = new AuthTestHost();
        using var anna = host.CreateClientFor(Anna);
        var productId = await CreateProductAsync(anna, "Kefir");

        await AssertProblemAsync(await UploadAsync(anna, productId, "not an image"u8.ToArray(), "notes.txt", "text/plain"), HttpStatusCode.UnsupportedMediaType, "photoNotAnImage");
        // Declared as JPEG, starts like one, but is not a picture.
        await AssertProblemAsync(await UploadAsync(anna, productId, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5]), HttpStatusCode.UnsupportedMediaType, "photoNotAnImage");
        await AssertProblemAsync(await UploadAsync(anna, productId, []), HttpStatusCode.BadRequest, "photoMissing");
        var huge = new byte[1600 * 1024];
        huge[0] = 0xFF;
        huge[1] = 0xD8;
        huge[2] = 0xFF;
        await AssertProblemAsync(await UploadAsync(anna, productId, huge), HttpStatusCode.RequestEntityTooLarge, "photoTooLarge");
        await AssertProblemAsync(await UploadAsync(anna, productId, new byte[3 * 1024 * 1024]), HttpStatusCode.RequestEntityTooLarge, null);
        await AssertProblemAsync(
            await anna.PostAsJsonAsync($"/api/products/{productId}/photos", new { photo = "x" }, Cancel),
            HttpStatusCode.BadRequest,
            "photoMissing");
        using (var twoFiles = new MultipartFormDataContent())
        {
            twoFiles.Add(Jpeg(TestImages.Jpeg(10, 10)), "photo", "a.jpg");
            twoFiles.Add(Jpeg(TestImages.Jpeg(10, 10)), "other", "b.jpg");
            await AssertProblemAsync(await anna.PostAsync($"/api/products/{productId}/photos", twoFiles, Cancel), HttpStatusCode.BadRequest, "photoMissing");
        }

        await AssertProblemAsync(await UploadAsync(anna, Guid.NewGuid(), TestImages.Jpeg(10, 10)), HttpStatusCode.NotFound, "productNotFound");

        Assert.Empty((await anna.GetFromJsonAsync<JsonElement>($"/api/products/{productId}/media", Cancel)).GetProperty("photos").EnumerateArray());
        if (LocalStorage)
        {
            var folder = Path.Combine(host.DataDirectory, "receipts", "products");
            Assert.True(!Directory.Exists(folder) || Directory.GetFileSystemEntries(folder).Length == 0);
        }
    }

    [Fact]
    public async Task ABarcodeBelongsToOneProductAndFindsIt()
    {
        await using var host = new AuthTestHost();
        using var anna = host.CreateClientFor(Anna);
        using var boris = host.CreateClientFor(Boris);
        var kefirId = await CreateProductAsync(anna, "Kefir");
        var milkId = await CreateProductAsync(anna, "Mleko");

        var added = await anna.PostAsJsonAsync($"/api/products/{kefirId}/barcodes", new { code = "5 900512-320045", source = "scan" }, Cancel);
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.Equal(Kefir, (await added.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("barcode").GetProperty("code").GetString());

        // Again on the same product: fine, nothing new. On another product: 409 naming the owner.
        var repeated = await boris.PostAsJsonAsync($"/api/products/{kefirId}/barcodes", new { code = Kefir, source = "manual" }, Cancel);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.False((await repeated.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("created").GetBoolean());
        var taken = await boris.PostAsJsonAsync($"/api/products/{milkId}/barcodes", new { code = Kefir }, Cancel);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        var problem = await taken.Content.ReadFromJsonAsync<JsonObject>(Cancel);
        Assert.Equal("barcodeTaken", problem!["code"]!.GetValue<string>());
        Assert.Equal(kefirId, problem["existingProductId"]!.GetValue<Guid>());

        // UPC-A is kept as the EAN-13 it equals; EAN-8 as is.
        var upc = await (await anna.PostAsJsonAsync($"/api/products/{milkId}/barcodes", new { code = UpcA, source = "openFoodFacts" }, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal("0" + UpcA, upc.GetProperty("barcode").GetProperty("code").GetString());
        Assert.Equal("openFoodFacts", upc.GetProperty("barcode").GetProperty("source").GetString());
        Assert.Equal(HttpStatusCode.Created, (await anna.PostAsJsonAsync($"/api/products/{milkId}/barcodes", new { code = Ean8 }, Cancel)).StatusCode);

        await AssertProblemAsync(await anna.PostAsJsonAsync($"/api/products/{milkId}/barcodes", new { code = "5900512320046" }, Cancel), HttpStatusCode.BadRequest, "invalidBarcode");
        await AssertProblemAsync(await anna.PostAsJsonAsync($"/api/products/{milkId}/barcodes", new { code = "12345" }, Cancel), HttpStatusCode.BadRequest, "invalidBarcode");
        Assert.Equal(HttpStatusCode.BadRequest, (await anna.PostAsJsonAsync($"/api/products/{milkId}/barcodes", new { code = Milk, source = "camera" }, Cancel)).StatusCode);
        await AssertProblemAsync(await anna.PostAsJsonAsync($"/api/products/{Guid.NewGuid()}/barcodes", new { code = Milk }, Cancel), HttpStatusCode.NotFound, "productNotFound");

        var lookup = await boris.GetFromJsonAsync<JsonElement>($"/api/barcodes/{UpcA}", Cancel);
        Assert.Equal("0" + UpcA, lookup.GetProperty("code").GetString());
        Assert.Equal(milkId, Assert.Single(lookup.GetProperty("productIds").EnumerateArray()).GetGuid());
        Assert.Empty((await boris.GetFromJsonAsync<JsonElement>($"/api/barcodes/{Milk}", Cancel)).GetProperty("productIds").EnumerateArray());
        await AssertProblemAsync(await boris.GetAsync("/api/barcodes/abc", Cancel), HttpStatusCode.BadRequest, "invalidBarcode");

        var media = await anna.GetFromJsonAsync<JsonElement>($"/api/products/{milkId}/media", Cancel);
        Assert.Equal(["0" + UpcA, Ean8], media.GetProperty("barcodes").EnumerateArray().Select(barcode => barcode.GetProperty("code").GetString()));

        // Removing it from another product's path is not found and changes nothing.
        await AssertProblemAsync(await boris.DeleteAsync($"/api/products/{milkId}/barcodes/{Kefir}", Cancel), HttpStatusCode.NotFound, "barcodeNotFound");
        Assert.Equal(HttpStatusCode.NoContent, (await boris.DeleteAsync($"/api/products/{kefirId}/barcodes/{Kefir}", Cancel)).StatusCode);
        Assert.Empty((await anna.GetFromJsonAsync<JsonElement>($"/api/barcodes/{Kefir}", Cancel)).GetProperty("productIds").EnumerateArray());
        Assert.Equal(HttpStatusCode.Created, (await boris.PostAsJsonAsync($"/api/products/{milkId}/barcodes", new { code = Kefir }, Cancel)).StatusCode);

        var audit = await AuditFieldsAsync(host, kefirId);
        Assert.Equal(2, audit.Count(entry => entry.Field == ProductAuditEntry.BarcodeField));
    }

    [Fact]
    public async Task RememberedCodesAreSharedAndGoAwayOnceLinked()
    {
        await using var host = new AuthTestHost();
        using var anna = host.CreateClientFor(Anna);
        using var boris = host.CreateClientFor(Boris);
        var productId = await CreateProductAsync(anna, "Kefir");

        var remembered = await anna.PutAsJsonAsync(
            $"/api/remembered-barcodes/{Kefir}",
            new { off = new { name = " Kefir naturalny ", brand = "Bakoma", quantity = "400 g", imageUrl = "https://evil.example/x.jpg" } },
            Cancel);
        Assert.Equal(HttpStatusCode.OK, remembered.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anna.PutAsJsonAsync($"/api/remembered-barcodes/{Milk}", new { off = (object?)null }, Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anna.PutAsync($"/api/remembered-barcodes/{UpcA}", null, Cancel)).StatusCode);

        var list = await boris.GetFromJsonAsync<JsonElement>("/api/remembered-barcodes", Cancel);
        Assert.Equal(3, list.GetArrayLength());
        var kefir = list.EnumerateArray().Single(entry => entry.GetProperty("code").GetString() == Kefir);
        Assert.Equal("Kefir naturalny", kefir.GetProperty("off").GetProperty("name").GetString());
        // Only Open Food Facts' own https images are kept.
        Assert.Equal(JsonValueKind.Null, kefir.GetProperty("off").GetProperty("imageUrl").ValueKind);
        Assert.Equal(Kefir, (await boris.GetFromJsonAsync<JsonElement>($"/api/barcodes/{Kefir}", Cancel)).GetProperty("remembered").GetProperty("code").GetString());

        // Linking the code forgets it; a code that belongs to a product cannot be remembered.
        Assert.Equal(HttpStatusCode.Created, (await boris.PostAsJsonAsync($"/api/products/{productId}/barcodes", new { code = Kefir }, Cancel)).StatusCode);
        Assert.Equal(2, (await anna.GetFromJsonAsync<JsonElement>("/api/remembered-barcodes", Cancel)).GetArrayLength());
        await AssertProblemAsync(await anna.PutAsync($"/api/remembered-barcodes/{Kefir}", null, Cancel), HttpStatusCode.Conflict, "barcodeTaken");

        Assert.Equal(HttpStatusCode.NoContent, (await anna.DeleteAsync($"/api/remembered-barcodes/{Milk}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anna.DeleteAsync($"/api/remembered-barcodes/{Milk}", Cancel)).StatusCode);
        Assert.Equal(["0" + UpcA], (await boris.GetFromJsonAsync<JsonElement>("/api/remembered-barcodes", Cancel))
            .EnumerateArray().Select(entry => entry.GetProperty("code").GetString()));
        await AssertProblemAsync(await anna.DeleteAsync("/api/remembered-barcodes/0000", Cancel), HttpStatusCode.BadRequest, "invalidBarcode");
    }

    [Fact]
    public async Task TheAvoidMarkKeepsItsFirstDate()
    {
        await using var host = new AuthTestHost();
        using var anna = host.CreateClientFor(Anna);
        var productId = await CreateProductAsync(anna, "Kefir");
        var since = new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

        var marked = await (await anna.PutAsJsonAsync($"/api/products/{productId}/avoid", new { avoid = true, since }, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(since, marked.GetProperty("avoid").GetProperty("since").GetDateTimeOffset());
        var again = await (await anna.PutAsJsonAsync($"/api/products/{productId}/avoid", new { avoid = true }, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(since, again.GetProperty("avoid").GetProperty("since").GetDateTimeOffset());

        // A date from the future is not believed.
        var cleared = await (await anna.PutAsJsonAsync($"/api/products/{productId}/avoid", new { avoid = false }, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("avoid").ValueKind);
        var future = await (await anna.PutAsJsonAsync($"/api/products/{productId}/avoid", new { avoid = true, since = DateTimeOffset.UtcNow.AddDays(30) }, Cancel))
            .Content.ReadFromJsonAsync<JsonElement>(Cancel);
        Assert.True(future.GetProperty("avoid").GetProperty("since").GetDateTimeOffset() <= DateTimeOffset.UtcNow.AddMinutes(1));

        var audit = await AuditFieldsAsync(host, productId);
        Assert.Equal(3, audit.Count(entry => entry.Field == ProductAuditEntry.AvoidField));
        await AssertProblemAsync(await anna.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}/avoid", new { avoid = true }, Cancel), HttpStatusCode.NotFound, "productNotFound");
    }

    [Fact]
    public async Task AMergeMovesPhotosBarcodesAndTheMark()
    {
        await using var host = new AuthTestHost();
        using var anna = host.CreateClientFor(Anna);
        var sourceId = await CreateProductAsync(anna, "KEFIR NAT");
        var targetId = await CreateProductAsync(anna, "Kefir naturalny");
        var sourcePhoto = await PhotoIdAsync(await UploadAsync(anna, sourceId, TestImages.Jpeg(300, 300), clientId: "same"));
        var targetPhoto = await PhotoIdAsync(await UploadAsync(anna, targetId, TestImages.Jpeg(300, 300), clientId: "same"));
        await anna.PostAsJsonAsync($"/api/products/{sourceId}/barcodes", new { code = Kefir }, Cancel);
        await anna.PutAsJsonAsync($"/api/products/{sourceId}/avoid", new { avoid = true }, Cancel);

        Assert.Equal(HttpStatusCode.OK, (await anna.PostAsync($"/api/products/{sourceId}/merge-into/{targetId}", null, Cancel)).StatusCode);

        var media = await anna.GetFromJsonAsync<JsonElement>($"/api/products/{targetId}/media", Cancel);
        Assert.Equal(targetPhoto, media.GetProperty("mainPhotoId").GetGuid());
        Assert.Equal(
            new[] { targetPhoto, sourcePhoto }.Order(),
            media.GetProperty("photos").EnumerateArray().Select(photo => photo.GetProperty("id").GetGuid()).Order());
        Assert.Single(media.GetProperty("photos").EnumerateArray(), photo => photo.GetProperty("isMain").GetBoolean());
        Assert.Equal(Kefir, Assert.Single(media.GetProperty("barcodes").EnumerateArray()).GetProperty("code").GetString());
        Assert.NotEqual(JsonValueKind.Null, media.GetProperty("avoid").ValueKind);
        Assert.Equal(targetId, Assert.Single((await anna.GetFromJsonAsync<JsonElement>($"/api/barcodes/{Kefir}", Cancel)).GetProperty("productIds").EnumerateArray()).GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await anna.GetAsync($"/api/product-photos/{sourcePhoto}", Cancel)).StatusCode);
        await AssertProblemAsync(await anna.GetAsync($"/api/products/{sourceId}/media", Cancel), HttpStatusCode.NotFound, "productNotFound");
    }

    [Fact]
    public async Task IdsOfAnotherProductReachNothing()
    {
        await using var host = new AuthTestHost();
        using var anna = host.CreateClientFor(Anna);
        var ownerId = await CreateProductAsync(anna, "Kefir");
        var otherId = await CreateProductAsync(anna, "Mleko");
        var photoId = await PhotoIdAsync(await UploadAsync(anna, ownerId, TestImages.Jpeg(200, 200)));

        // A photo id used under another product's path is not that product's photo.
        await AssertProblemAsync(await anna.DeleteAsync($"/api/products/{otherId}/photos/{photoId}", Cancel), HttpStatusCode.NotFound, "photoNotFound");
        await AssertProblemAsync(await anna.PutAsync($"/api/products/{otherId}/photos/{photoId}/main", null, Cancel), HttpStatusCode.NotFound, "photoNotFound");
        await AssertProblemAsync(await anna.DeleteAsync($"/api/products/{Guid.NewGuid()}/photos/{photoId}", Cancel), HttpStatusCode.NotFound, "productNotFound");
        Assert.Equal(HttpStatusCode.NotFound, (await anna.GetAsync($"/api/product-photos/{Guid.NewGuid()}", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anna.GetAsync($"/api/product-photos/{Guid.NewGuid()}/thumbnail", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anna.GetAsync("/api/product-photos/not-a-guid", Cancel)).StatusCode);
        // A storage key or a path is never accepted in place of an id.
        Assert.Equal(HttpStatusCode.NotFound, (await anna.GetAsync("/api/product-photos/..%2F..%2Fpurchases.db", Cancel)).StatusCode);

        var media = await anna.GetFromJsonAsync<JsonElement>($"/api/products/{ownerId}/media", Cancel);
        Assert.Equal(photoId, media.GetProperty("mainPhotoId").GetGuid());
        Assert.Empty((await anna.GetFromJsonAsync<JsonElement>($"/api/products/{otherId}/media", Cancel)).GetProperty("photos").EnumerateArray());
    }

    [Fact]
    public async Task EveryEndpointRequiresAnApprovedUser()
    {
        var openFoodFacts = new FakeOpenFoodFacts();
        await using var host = new AuthTestHost(configureServices: services => services.AddSingleton<IOpenFoodFactsClient>(openFoodFacts));
        using var anna = host.CreateClientFor(Anna);
        var productId = await CreateProductAsync(anna, "Kefir");
        var photoId = await PhotoIdAsync(await UploadAsync(anna, productId, TestImages.Jpeg(200, 200)));
        await anna.PostAsJsonAsync($"/api/products/{productId}/barcodes", new { code = Kefir }, Cancel);
        await anna.PutAsync($"/api/remembered-barcodes/{Milk}", null, Cancel);

        var callers = new (string? Token, HttpStatusCode Expected)[]
        {
            (null, HttpStatusCode.Unauthorized),
            (AuthTestHost.Token(key: AuthTestHost.ForeignKey), HttpStatusCode.Unauthorized),
            (AuthTestHost.Token(expires: DateTime.UtcNow.AddMinutes(-10)), HttpStatusCode.Unauthorized),
            (AuthTestHost.Token(access: "pending", subject: BorisId), HttpStatusCode.Forbidden),
            (AuthTestHost.Token(access: "blocked", subject: BorisId), HttpStatusCode.Forbidden)
        };
        foreach (var (token, expected) in callers)
        {
            using var client = host.CreateClientFor(token);
            var calls = new Func<Task<HttpResponseMessage>>[]
            {
                () => client.GetAsync($"/api/products/{productId}/media", Cancel),
                () => UploadAsync(client, productId, TestImages.Jpeg(50, 50)),
                () => client.DeleteAsync($"/api/products/{productId}/photos/{photoId}", Cancel),
                () => client.PutAsync($"/api/products/{productId}/photos/{photoId}/main", null, Cancel),
                () => client.GetAsync($"/api/product-photos/{photoId}", Cancel),
                () => client.GetAsync($"/api/product-photos/{photoId}/thumbnail", Cancel),
                () => client.PostAsJsonAsync($"/api/products/{productId}/barcodes", new { code = Ean8 }, Cancel),
                () => client.DeleteAsync($"/api/products/{productId}/barcodes/{Kefir}", Cancel),
                () => client.PutAsJsonAsync($"/api/products/{productId}/avoid", new { avoid = true }, Cancel),
                () => client.GetAsync($"/api/barcodes/{Kefir}", Cancel),
                () => client.GetAsync("/api/remembered-barcodes", Cancel),
                () => client.PutAsync($"/api/remembered-barcodes/{UpcA}", null, Cancel),
                () => client.DeleteAsync($"/api/remembered-barcodes/{Milk}", Cancel),
                () => client.GetAsync($"/api/open-food-facts/{Kefir}", Cancel)
            };
            foreach (var call in calls)
            {
                Assert.Equal(expected, (await call()).StatusCode);
            }
        }

        // Nothing the refused calls asked for happened; Open Food Facts was never asked.
        var media = await anna.GetFromJsonAsync<JsonElement>($"/api/products/{productId}/media", Cancel);
        Assert.Single(media.GetProperty("photos").EnumerateArray());
        Assert.Equal([Kefir], media.GetProperty("barcodes").EnumerateArray().Select(barcode => barcode.GetProperty("code").GetString()));
        Assert.Equal(JsonValueKind.Null, media.GetProperty("avoid").ValueKind);
        Assert.Equal([Milk], (await anna.GetFromJsonAsync<JsonElement>("/api/remembered-barcodes", Cancel)).EnumerateArray().Select(entry => entry.GetProperty("code").GetString()));
        Assert.Empty(openFoodFacts.Codes);
    }

    [Fact]
    public async Task OpenFoodFactsIsAskedWithTheNormalizedBarcodeOnly()
    {
        var openFoodFacts = new FakeOpenFoodFacts();
        await using var host = new AuthTestHost(configureServices: services => services.AddSingleton<IOpenFoodFactsClient>(openFoodFacts));
        using var anna = host.CreateClientFor(Anna);

        var found = await anna.GetFromJsonAsync<JsonElement>($"/api/open-food-facts/{UpcA}", Cancel);
        Assert.Equal("found", found.GetProperty("status").GetString());
        Assert.Equal("Kefir naturalny", found.GetProperty("product").GetProperty("name").GetString());
        var missing = await anna.GetFromJsonAsync<JsonElement>($"/api/open-food-facts/{Milk}", Cancel);
        Assert.Equal("notFound", missing.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, missing.GetProperty("product").ValueKind);
        await AssertProblemAsync(await anna.GetAsync("/api/open-food-facts/123", Cancel), HttpStatusCode.BadRequest, "invalidBarcode");

        Assert.Equal(["0" + UpcA, Milk], openFoodFacts.Codes);
    }

    private sealed class FakeOpenFoodFacts : IOpenFoodFactsClient
    {
        public List<string> Codes { get; } = [];

        public Task<OpenFoodFactsLookup> LookupAsync(string code, CancellationToken cancellationToken)
        {
            Codes.Add(code);
            return Task.FromResult(code == "0" + UpcA
                ? new OpenFoodFactsLookup(OpenFoodFactsStatus.Found, new OpenFoodFactsProduct("Kefir naturalny", "Bakoma", "400 g", null))
                : OpenFoodFactsLookup.NotFound);
        }
    }

    private static async Task<Guid> CreateProductAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/products", new { name }, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> PhotoIdAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, response.StatusCode.ToString());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Cancel)).GetProperty("photo").GetProperty("id").GetGuid();
    }

    private static ByteArrayContent Jpeg(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return content;
    }

    private static Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        Guid productId,
        byte[] bytes,
        string fileName = "photo.jpg",
        string contentType = "image/jpeg",
        string? clientId = null)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { content, "photo", fileName } };
        if (clientId is not null)
        {
            form.Add(new StringContent(clientId), "clientId");
        }

        return client.PostAsync($"/api/products/{productId}/photos", form, Cancel);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string? code)
    {
        Assert.Equal(status, response.StatusCode);
        if (code is null)
        {
            return;
        }

        var problem = await response.Content.ReadFromJsonAsync<JsonObject>(Cancel);
        Assert.Equal(code, problem!["code"]?.GetValue<string>());
    }

    private static async Task<List<(string Field, string Actor)>> AuditFieldsAsync(AuthTestHost host, Guid productId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        return (await context.ProductAuditEntries
                .AsNoTracking()
                .Where(entry => entry.ProductId == productId)
                .Select(entry => new { entry.FieldName, entry.Actor })
                .ToListAsync(Cancel))
            .Select(entry => (entry.FieldName, entry.Actor))
            .ToList();
    }
}

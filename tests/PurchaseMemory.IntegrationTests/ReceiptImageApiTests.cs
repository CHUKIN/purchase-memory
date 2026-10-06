using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Infrastructure.Images;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Photos through the real HTTP API on whichever backends the run uses
/// (SQLite + folder by default; Postgres/S3 when opted in).
/// </summary>
public sealed class ReceiptImageApiTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheAppShowsTheDisplayCopyAndTheOriginalStaysDownloadable()
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token());
        var original = TestImages.WithExif(TestImages.Jpeg(3000, 4000), orientation: 1);

        var receiptId = await UploadAsync(client, original);

        using var display = await client.GetAsync($"/api/receipts/{receiptId}/image", Cancel);
        Assert.Equal(HttpStatusCode.OK, display.StatusCode);
        Assert.Equal("image/jpeg", display.Content.Headers.ContentType?.MediaType);
        var displayBytes = await display.Content.ReadAsByteArrayAsync(Cancel);
        using (var decoded = TestImages.Decode(displayBytes))
        {
            Assert.Equal((1800, 2400), (decoded.Width, decoded.Height));
        }

        Assert.False(TestImages.ContainsSequence(displayBytes, "SecretCam"u8));

        using var part = await client.GetAsync($"/api/receipts/{receiptId}/images/0", Cancel);
        Assert.Equal(displayBytes, await part.Content.ReadAsByteArrayAsync(Cancel));

        using var download = await client.GetAsync($"/api/receipts/{receiptId}/images/0?original=true", Cancel);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(original, await download.Content.ReadAsByteArrayAsync(Cancel));
    }

    [Fact]
    public async Task PhotosAreNotServedWithoutAnApprovedToken()
    {
        await using var host = new AuthTestHost();
        using var approved = host.CreateClientFor(AuthTestHost.Token());
        var receiptId = await UploadAsync(approved, TestImages.Jpeg(400, 300));

        using var anonymous = host.CreateClientFor(token: null);
        using var pending = host.CreateClientFor(AuthTestHost.Token(access: "pending"));
        using var forged = host.CreateClientFor(AuthTestHost.Token(key: AuthTestHost.ForeignKey));
        foreach (var client in new[] { anonymous, pending, forged })
        {
            foreach (var path in new[] { "image", "images/0", "images/0?original=true" })
            {
                using var response = await client.GetAsync($"/api/receipts/{receiptId}/{path}", Cancel);
                Assert.True(
                    response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden,
                    $"{path}: {(int)response.StatusCode}");
            }
        }
    }

    [Fact]
    public async Task OptedInBackendsAreReallyUsed()
    {
        await using var host = new AuthTestHost();
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var blobs = scope.ServiceProvider.GetRequiredService<IReceiptBlobStorage>();

        Assert.Equal(
            ExternalTestServices.PostgresAdminConnectionString is null
                ? "Microsoft.EntityFrameworkCore.Sqlite"
                : "Npgsql.EntityFrameworkCore.PostgreSQL",
            context.Database.ProviderName);
        Assert.Equal(
            Environment.GetEnvironmentVariable(ExternalTestServices.S3UrlVariable) is { Length: > 0 }
                ? typeof(S3ReceiptBlobStorage)
                : typeof(LocalReceiptBlobStorage),
            blobs.GetType());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync(Cancel));

        // Each provider's migrations must describe the current model exactly.
        Assert.False(context.Database.HasPendingModelChanges());
    }

    private static async Task<Guid> UploadAsync(HttpClient client, byte[] image)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(image);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(content, "images", "receipt.jpg");
        using var response = await client.PostAsync("/api/receipts", form, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancel);
        return body.GetProperty("receipt").GetProperty("id").GetGuid();
    }
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Infrastructure.Persistence;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// Who uploaded a receipt and who corrected it is taken from the validated
/// token, and the new columns keep old rows readable.
/// </summary>
public sealed class ReceiptActorAuditTests
{
    private const string MigrationBeforeActors = "20260924142126_AddReceiptLineKind";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UploadRecordsTheSignedInUser()
    {
        await using var host = new AuthTestHost();
        using var client = host.CreateClientFor(AuthTestHost.Token(subject: "user-anna", email: "anna@example.com"));

        var form = new MultipartFormDataContent();
        var image = new ByteArrayContent(JpegLike);
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        form.Add(image, "images", "receipt.jpg");
        var response = await client.PostAsync("/api/receipts", form, Cancel);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancel));

        // The host's own context, so the check follows whichever database the
        // host runs on (SQLite by default, Postgres when opted in).
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<PurchaseMemoryDbContext>();
        var receipt = await context.ReceiptCaptures.SingleAsync(Cancel);
        Assert.Equal("user-anna", receipt.UploadedByUserId);
        Assert.Equal("anna@example.com", receipt.UploadedByEmail);
    }

    [Fact]
    public async Task OldRowsSurviveTheMigrationWithoutAnActor()
    {
        var directory = Path.Combine(Path.GetTempPath(), "purchase-memory-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var options = new DbContextOptionsBuilder<PurchaseMemoryDbContext>()
                .UseSqlite($"Data Source={Path.Combine(directory, "migration.db")};Pooling=False")
                .Options;
            var receiptId = Guid.NewGuid();
            await using (var oldContext = new PurchaseMemoryDbContext(options))
            {
                await oldContext.Database.MigrateAsync(MigrationBeforeActors, Cancel);
                await oldContext.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO receipt_captures
                        (Id, ImageStorageKey, ImageContentType, ImageSizeBytes, ImageSha256, Status, CreatedAt, UpdatedAt)
                    VALUES
                        ({receiptId}, {"old/receipt.jpg"}, {"image/jpeg"}, {321L}, {new string('c', 64)},
                         {0}, {1_785_758_400_000L}, {1_785_758_400_000L});
                    INSERT INTO receipt_review_audit_entries
                        (Id, ReceiptCaptureId, Actor, FieldName, ChangeType, OldValue, NewValue, OccurredAt)
                    VALUES
                        ({Guid.NewGuid()}, {receiptId}, {"Owner"}, {"MerchantName"}, {"Modified"}, {"A"}, {"B"}, {1_785_758_400_000L});
                    """, Cancel);
            }

            await using var upgraded = new PurchaseMemoryDbContext(options);
            await upgraded.Database.MigrateAsync(Cancel);
            var receipt = await upgraded.ReceiptCaptures
                .Include(capture => capture.ReviewAuditEntries)
                .SingleAsync(capture => capture.Id == receiptId, Cancel);

            Assert.Null(receipt.UploadedByUserId);
            Assert.Null(receipt.UploadedByEmail);
            var entry = Assert.Single(receipt.ReviewAuditEntries);
            Assert.Equal("Owner", entry.Actor);
            Assert.Null(entry.ActorEmail);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // The store only sniffs the JPEG signature; nothing decodes the pixels here.
    private static readonly byte[] JpegLike = [0xff, 0xd8, 0xff, 0xe0, .. new byte[64], 0xff, 0xd9];
}

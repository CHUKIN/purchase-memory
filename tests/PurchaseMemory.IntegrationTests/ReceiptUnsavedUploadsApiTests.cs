using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// `GET /api/receipts/my-unsaved` lets the capture screen rebuild a batch
/// after a reload, and `?process=true` on an exact duplicate that was never
/// queued lets a resumed batch finish the job without a second receipt.
/// </summary>
public sealed class ReceiptUnsavedUploadsApiTests
{
    private const string Anna = "11111111-1111-1111-1111-111111111111";
    private const string Boris = "22222222-2222-2222-2222-222222222222";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListRequiresAnApprovedSignedInUser()
    {
        await using var host = Host(new RecordingDispatcher());
        using var anonymous = host.CreateClientFor(null);
        using var forged = host.CreateClientFor(AuthTestHost.Token(key: AuthTestHost.ForeignKey));
        using var pending = host.CreateClientFor(AuthTestHost.Token(access: "pending"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/receipts/my-unsaved", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await forged.GetAsync("/api/receipts/my-unsaved", Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await pending.GetAsync("/api/receipts/my-unsaved", Cancel)).StatusCode);
    }

    [Fact]
    public async Task EachUserSeesOnlyTheirOwnUploads()
    {
        await using var host = Host(new RecordingDispatcher());
        using var anna = host.CreateClientFor(AuthTestHost.Token(subject: Anna, email: "anna@example.com"));
        using var boris = host.CreateClientFor(AuthTestHost.Token(subject: Boris, email: "boris@example.com"));

        var first = await UploadAsync(anna, 10);
        var second = await UploadAsync(anna, 11);
        var borisReceipt = await UploadAsync(boris, 12);

        var annaList = await ListAsync(anna);
        var borisList = await ListAsync(boris);

        Assert.Equal(new[] { second.Id, first.Id }, annaList.Select(receipt => receipt.Id));
        Assert.All(annaList, receipt => Assert.Equal("queued", receipt.Status));
        Assert.Equal(new[] { borisReceipt.Id }, borisList.Select(receipt => receipt.Id));
    }

    [Fact]
    public async Task ResendingAReceiptThatWasNeverQueuedQueuesItOnce()
    {
        var dispatcher = new RecordingDispatcher();
        await using var host = Host(dispatcher);
        using var client = host.CreateClientFor(AuthTestHost.Token());

        // The first attempt stored the receipt but never queued it.
        var stored = await UploadAsync(client, 20, process: false);
        Assert.Equal("uploaded", stored.Status);

        var resent = await client.PostAsync("/api/receipts?process=true", Form(20), Cancel);
        Assert.Equal(HttpStatusCode.OK, resent.StatusCode);
        var resumed = await ReadUploadAsync(resent);
        Assert.True(resumed.Duplicate);
        Assert.Equal(stored.Id, resumed.Id);
        Assert.Equal("queued", resumed.Status);

        // Already queued: sending it a third time does not queue it again.
        var again = await ReadUploadAsync(await client.PostAsync("/api/receipts?process=true", Form(20), Cancel));
        Assert.Equal(stored.Id, again.Id);
        Assert.Equal(new[] { stored.Id }, dispatcher.Enqueued);
        Assert.Single(await ListAsync(client));
    }

    private static AuthTestHost Host(RecordingDispatcher dispatcher) => new(configureServices: services =>
        services.Replace(ServiceDescriptor.Singleton<IReceiptProcessingDispatcher>(dispatcher)));

    private static async Task<(Guid Id, string Status, bool Duplicate)> UploadAsync(HttpClient client, byte seed, bool process = true)
    {
        var response = await client.PostAsync(process ? "/api/receipts?process=true" : "/api/receipts", Form(seed), Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadUploadAsync(response);
    }

    private static async Task<IReadOnlyList<(Guid Id, string Status)>> ListAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/receipts/my-unsaved", Cancel);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        return document.RootElement.EnumerateArray()
            .Select(receipt => (receipt.GetProperty("id").GetGuid(), receipt.GetProperty("status").GetString()!))
            .ToList();
    }

    private static MultipartFormDataContent Form(byte seed)
    {
        // The store only sniffs the JPEG signature; nothing decodes the pixels.
        byte[] bytes = [0xff, 0xd8, 0xff, 0xe0, seed, .. new byte[64], 0xff, 0xd9];
        var image = new ByteArrayContent(bytes);
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { image, "images", "receipt.jpg" } };
    }

    private static async Task<(Guid Id, string Status, bool Duplicate)> ReadUploadAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancel));
        var receipt = document.RootElement.GetProperty("receipt");
        return (
            receipt.GetProperty("id").GetGuid(),
            receipt.GetProperty("status").GetString()!,
            document.RootElement.GetProperty("exactDuplicate").GetBoolean());
    }

    /// <summary>Records enqueues and never hands work to the background worker.</summary>
    private sealed class RecordingDispatcher : IReceiptProcessingDispatcher
    {
        private readonly ConcurrentQueue<Guid> _enqueued = new();

        public IReadOnlyList<Guid> Enqueued => [.. _enqueued];

        public ValueTask EnqueueAsync(Guid receiptId, CancellationToken cancellationToken)
        {
            _enqueued.Enqueue(receiptId);
            return ValueTask.CompletedTask;
        }

        public async ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return Guid.Empty;
        }
    }
}

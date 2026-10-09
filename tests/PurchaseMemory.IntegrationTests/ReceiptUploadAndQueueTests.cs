using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PurchaseMemory.Application.Receipts;

namespace PurchaseMemory.IntegrationTests;

/// <summary>
/// `POST /api/receipts?process=true` uploads and queues recognition in one
/// rate-limited request, which is what batch capture ("separate receipts")
/// relies on to fit a burst of receipts into the upload budget.
/// </summary>
public sealed class ReceiptUploadAndQueueTests
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProcessFlagQueuesTheNewReceiptInTheSameRequest()
    {
        var dispatcher = new RecordingDispatcher();
        await using var host = new AuthTestHost(configureServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IReceiptProcessingDispatcher>(dispatcher)));
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var response = await client.PostAsync("/api/receipts?process=true", Form(1), Cancel);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var (id, status, duplicate) = await ReadAsync(response);
        Assert.Equal("queued", status);
        Assert.False(duplicate);
        Assert.Equal([id], dispatcher.Enqueued);
    }

    [Fact]
    public async Task WithoutTheFlagTheReceiptStaysUploaded()
    {
        var dispatcher = new RecordingDispatcher();
        await using var host = new AuthTestHost(configureServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IReceiptProcessingDispatcher>(dispatcher)));
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var response = await client.PostAsync("/api/receipts", Form(2), Cancel);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("uploaded", (await ReadAsync(response)).Status);
        Assert.Empty(dispatcher.Enqueued);
    }

    [Fact]
    public async Task AnExactDuplicateIsNotQueuedAgain()
    {
        var dispatcher = new RecordingDispatcher();
        await using var host = new AuthTestHost(configureServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IReceiptProcessingDispatcher>(dispatcher)));
        using var client = host.CreateClientFor(AuthTestHost.Token());

        var first = await ReadAsync(await client.PostAsync("/api/receipts?process=true", Form(3), Cancel));
        var again = await client.PostAsync("/api/receipts?process=true", Form(3), Cancel);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var second = await ReadAsync(again);
        Assert.True(second.Duplicate);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal([first.Id], dispatcher.Enqueued);
    }

    [Fact]
    public async Task UploadWithProcessSpendsOneTokenNotTwo()
    {
        var dispatcher = new RecordingDispatcher();
        await using var host = new AuthTestHost(
            settings: new Dictionary<string, string?>
            {
                ["RateLimiting:ReceiptUploads:TokenLimit"] = "2",
                ["RateLimiting:ReceiptUploads:ReplenishmentPeriodSeconds"] = "3600"
            },
            configureServices: services =>
                services.Replace(ServiceDescriptor.Singleton<IReceiptProcessingDispatcher>(dispatcher)));
        using var client = host.CreateClientFor(AuthTestHost.Token());

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/receipts?process=true", Form(4), Cancel)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsync("/api/receipts?process=true", Form(5), Cancel)).StatusCode);
        var limited = await client.PostAsync("/api/receipts?process=true", Form(6), Cancel);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(2, dispatcher.Enqueued.Count);
    }

    private static MultipartFormDataContent Form(byte seed)
    {
        // The store only sniffs the JPEG signature; nothing decodes the pixels.
        byte[] bytes = [0xff, 0xd8, 0xff, 0xe0, seed, .. new byte[64], 0xff, 0xd9];
        var image = new ByteArrayContent(bytes);
        image.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { image, "images", "receipt.jpg" } };
    }

    private static async Task<(Guid Id, string Status, bool Duplicate)> ReadAsync(HttpResponseMessage response)
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

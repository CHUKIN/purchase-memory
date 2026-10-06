using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PurchaseMemory.Api.Endpoints;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.IntegrationTests;

public sealed class ReceiptEndpointLimitTests
{
    [Fact]
    public async Task UploadEndpointAcceptsDocumentedSixtyMebibytesPlusMultipartOverhead()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IReceiptProcessingService>(_ => null!);
        builder.Services.AddSingleton<IReceiptImageStore>(_ => null!);
        builder.Services.AddSingleton<IReceiptRecognitionStatusProvider>(_ => null!);
        await using var app = builder.Build();
        app.MapReceiptEndpoints();

        var uploadEndpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == "/api/receipts"
                && endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") == true);
        var requestLimit = uploadEndpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>();
        var formLimit = uploadEndpoint.Metadata.GetMetadata<RequestFormLimitsAttribute>();

        Assert.NotNull(requestLimit);
        Assert.NotNull(formLimit);
        Assert.Equal(ReceiptCaptureLimits.MaximumMultipartRequestSizeBytes, requestLimit.MaxRequestBodySize);
        Assert.Equal(ReceiptCaptureLimits.MaximumMultipartRequestSizeBytes, formLimit.MultipartBodyLengthLimit);
        Assert.True(requestLimit.MaxRequestBodySize > ReceiptCaptureLimits.MaximumTotalSizeBytes);
    }
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Infrastructure.Recognition;

namespace PurchaseMemory.IntegrationTests;

public sealed class ReceiptRecognitionDependencyInjectionTests
{
    [Fact]
    public async Task ResolvesCompatibilityExtractorAndStatusWithoutRealOllama()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<ReceiptRecognitionOptions>().Configure(options =>
        {
            options.Pipeline = "ollama";
            options.FallbackPipelines = [];
            options.Mode = ReceiptRecognitionMode.Vision;
            options.VisionModel = "fake-vision";
        });
        services.AddSingleton(new ReceiptRecognitionPipelineDescriptor("apple"));
        services.AddSingleton(new ReceiptRecognitionPipelineDescriptor("ollama"));
        services.AddSingleton<IValidateOptions<ReceiptRecognitionOptions>, ReceiptRecognitionOptionsValidator>();
        services.AddSingleton(new HttpClient(new TagsHandler())
        {
            BaseAddress = new Uri("http://ollama.test/", UriKind.Absolute)
        });
        services.AddScoped<OllamaStructuredVisionProvider>(serviceProvider =>
            new OllamaStructuredVisionProvider(
                serviceProvider.GetRequiredService<HttpClient>(),
                serviceProvider.GetRequiredService<IOptions<ReceiptRecognitionOptions>>(),
                serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OllamaStructuredVisionProvider>>()));
        services.AddScoped<IStructuredVisionProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<OllamaStructuredVisionProvider>());
        services.AddScoped<IStructuredVisionProviderResolver, StructuredVisionProviderResolver>();
        services.AddScoped<IReceiptAnalysisWorkflow, ReceiptAnalysisWorkflow>();
        services.AddScoped<AppleReceiptExtractor>();
        services.AddScoped<OllamaReceiptExtractor>();
        services.AddScoped<IReceiptRecognitionPipeline>(serviceProvider =>
            serviceProvider.GetRequiredService<AppleReceiptExtractor>());
        services.AddScoped<IReceiptRecognitionPipeline>(serviceProvider =>
            serviceProvider.GetRequiredService<OllamaReceiptExtractor>());
        services.AddScoped<ReceiptExtractorRouter>();
        services.AddScoped<IReceiptExtractor>(serviceProvider =>
            serviceProvider.GetRequiredService<ReceiptExtractorRouter>());
        services.AddScoped<IReceiptRecognitionStatusProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<ReceiptExtractorRouter>());

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        Assert.IsType<ReceiptExtractorRouter>(
            scope.ServiceProvider.GetRequiredService<IReceiptExtractor>());
        var status = await scope.ServiceProvider
            .GetRequiredService<IReceiptRecognitionStatusProvider>()
            .GetStatusAsync(TestContext.Current.CancellationToken);
        Assert.True(status.Available);
        Assert.Equal(["fake-vision"], status.Models);
    }

    private sealed class TagsHandler : HttpMessageHandler
    {
        private static readonly string[] VisionCapabilities = ["vision"];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = request.RequestUri?.AbsolutePath switch
            {
                "/api/tags" => JsonContent.Create(new
                {
                    models = new[] { new { name = "fake-vision" } }
                }),
                "/api/show" => JsonContent.Create(new
                {
                    capabilities = VisionCapabilities
                }),
                _ => throw new InvalidOperationException(
                    $"Unexpected Ollama path: {request.RequestUri?.AbsolutePath}")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}

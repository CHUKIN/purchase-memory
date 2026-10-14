using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol.Protocol;
using PurchaseMemory.Api.Auth;
using PurchaseMemory.Api.Configuration;
using PurchaseMemory.Api.Endpoints;
using PurchaseMemory.Api.Errors;
using PurchaseMemory.Api.Logging;
using PurchaseMemory.Api.Mcp;
using PurchaseMemory.Api.Processing;
using PurchaseMemory.Application.Analytics;
using PurchaseMemory.Application.Impressions;
using PurchaseMemory.Application.Products;
using PurchaseMemory.Application.Products.Categories;
using PurchaseMemory.Application.Products.Tags;
using PurchaseMemory.Application.Purchases;
using PurchaseMemory.Application.Receipts;
using PurchaseMemory.Application.ReviewQueue;
using PurchaseMemory.Infrastructure;
using PurchaseMemory.Infrastructure.Analytics;
using PurchaseMemory.Infrastructure.Categorization;
using PurchaseMemory.Infrastructure.Images;
using PurchaseMemory.Infrastructure.Persistence;
using PurchaseMemory.Infrastructure.Processing;
using PurchaseMemory.Infrastructure.Recognition;
using PurchaseMemory.Infrastructure.Tagging;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);
builder.UseHostingPort();
var runtimePaths = RuntimePaths.Create(builder.Environment, builder.Configuration);

builder.Services.AddSingleton(runtimePaths);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IProductMatcher, ProductMatcher>();
builder.Services.AddScoped<IProductCatalogService, ProductCatalogService>();
builder.Services.AddScoped<IPurchaseDraftService, PurchaseDraftService>();
builder.Services.AddScoped<IPurchaseEditService, PurchaseEditService>();
builder.Services.AddScoped<IProductImpressionService, ProductImpressionService>();
builder.Services.AddScoped<IReceiptProcessingService, ReceiptProcessingService>();
builder.Services.AddScoped<IProductCategoryService, ProductCategoryService>();
builder.Services.AddScoped<IProductTagService, ProductTagService>();
builder.Services
    .AddOptions<TagSuggestionOptions>()
    .Bind(builder.Configuration.GetSection(TagSuggestionOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<TagSuggestionOptions>,
    TagSuggestionOptionsValidator>();
builder.Services.AddSingleton<AnthropicTagSuggester>();
builder.Services.AddSingleton<ITagSuggester>(serviceProvider =>
    serviceProvider.GetRequiredService<AnthropicTagSuggester>());
builder.Services.AddScoped<IProductTagSuggestionService, ProductTagSuggestionService>();
builder.Services.AddScoped<IProductCategorizationService, ProductCategorizationService>();
builder.Services
    .AddOptions<ProductCategorizationOptions>()
    .Bind(builder.Configuration.GetSection(ProductCategorizationOptions.SectionName));
builder.Services.AddSingleton<OpenAiProductCategorizer>();
builder.Services.AddSingleton<IProductCategorizer>(serviceProvider =>
    serviceProvider.GetRequiredService<OpenAiProductCategorizer>());
// Registered before the infrastructure, whose no-op default then stays unused.
builder.Services.AddSingleton<ChannelProductCategorizationDispatcher>();
builder.Services.AddSingleton<IProductCategorizationDispatcher>(serviceProvider =>
    serviceProvider.GetRequiredService<ChannelProductCategorizationDispatcher>());
builder.Services.AddHostedService<ProductCategorizationWorker>();
builder.Services.AddSingleton<CategorizationBackfillGate>();
builder.Services
    .AddOptions<AnalyticsOptions>()
    .Bind(builder.Configuration.GetSection(AnalyticsOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<AnalyticsOptions>,
    AnalyticsOptionsValidator>();
builder.Services.AddSingleton<AnthropicAnalyticsQueryPlanner>();
builder.Services.AddSingleton<OpenAiAnalyticsQueryPlanner>();
builder.Services.AddSingleton<IAnalyticsQueryPlanner>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AnalyticsOptions>>().Value;
    var planners = options.PlannerOrder()
        .Select(provider => (provider, provider == AnthropicAnalyticsQueryPlanner.ProviderId
            ? (IAnalyticsQueryPlanner)serviceProvider.GetRequiredService<AnthropicAnalyticsQueryPlanner>()
            : serviceProvider.GetRequiredService<OpenAiAnalyticsQueryPlanner>()))
        .ToList();
    return new AnalyticsQueryPlannerRouter(planners, serviceProvider.GetRequiredService<ILogger<AnalyticsQueryPlannerRouter>>());
});
builder.Services.AddSingleton(serviceProvider => new AnalyticsSettings(TimeZoneInfo.FindSystemTimeZoneById(
    serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AnalyticsOptions>>().Value.TimeZone)));
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<IAnalyticsHistoryService, AnalyticsHistoryService>();
builder.Services.AddScoped<IReviewQueueService, ReviewQueueService>();
var databaseSettings = StorageSetup.ResolveDatabase(builder.Configuration, runtimePaths);
builder.Services.AddPurchaseMemoryInfrastructure(databaseSettings);
builder.Services.AddProblemDetails();
// Binding failures (malformed JSON, wrong types) reach ApiExceptionHandler in every
// environment, so clients always get a 400 ProblemDetails instead of an empty 400 or a 500.
builder.Services.Configure<Microsoft.AspNetCore.Routing.RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddPurchaseMemoryAuth(builder.Configuration, builder.Environment);
builder.Services.AddReverseProxySupport(builder.Configuration);
builder.Services.AddReceiptUploadRateLimiting(builder.Configuration);

builder.Services.AddReceiptImageStorage(builder.Configuration, runtimePaths);
builder.Services
    .AddOptions<ReceiptRecognitionOptions>()
    .Bind(builder.Configuration.GetSection(ReceiptRecognitionOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton(new ReceiptRecognitionPipelineDescriptor("apple"));
builder.Services.AddSingleton(new ReceiptRecognitionPipelineDescriptor("ollama"));
builder.Services.AddSingleton(new ReceiptRecognitionPipelineDescriptor(
    OpenAiStructuredVisionProvider.ProviderId));
builder.Services.AddSingleton(new ReceiptRecognitionPipelineDescriptor(
    AnthropicStructuredVisionProvider.ProviderId));
builder.Services.AddSingleton(new ReceiptRecognitionPipelineDescriptor(
    AppleGeometryOllamaExtractor.PipelineId));
builder.Services.AddSingleton<
    Microsoft.Extensions.Options.IValidateOptions<ReceiptRecognitionOptions>,
    ReceiptRecognitionOptionsValidator>();
builder.Services.PostConfigure<ReceiptRecognitionOptions>(options =>
{
    if (!Path.IsPathFullyQualified(options.AppleSidecarPath))
    {
        options.AppleSidecarPath = Path.GetFullPath(
            options.AppleSidecarPath,
            builder.Environment.ContentRootPath);
    }
});
builder.Services.AddSingleton<IReceiptProcessingDispatcher, ChannelReceiptProcessingDispatcher>();
builder.Services.AddHttpClient<OllamaStructuredVisionProvider>((serviceProvider, client) =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<ReceiptRecognitionOptions>>()
        .Value;
    client.BaseAddress = new Uri(options.BaseAddress.TrimEnd('/') + '/', UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
});
builder.Services.AddScoped<IStructuredVisionProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<OllamaStructuredVisionProvider>());
builder.Services.AddSingleton<OpenAiStructuredVisionProvider>();
builder.Services.AddSingleton<IStructuredVisionProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<OpenAiStructuredVisionProvider>());
builder.Services.AddSingleton<AnthropicStructuredVisionProvider>();
builder.Services.AddSingleton<IStructuredVisionProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<AnthropicStructuredVisionProvider>());
builder.Services.AddScoped<IStructuredVisionProviderResolver, StructuredVisionProviderResolver>();
builder.Services.AddScoped<IReceiptAnalysisWorkflow, ReceiptAnalysisWorkflow>();
// "Apple reads, Ollama structures": its own HTTP client with its own timeout,
// because the text parser is a much slower call than the vision route and the
// configured tag may be a cloud model with no latency guarantee.
builder.Services.AddHttpClient<OllamaStructuredTextProvider>((serviceProvider, client) =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<ReceiptRecognitionOptions>>()
        .Value;
    client.BaseAddress = new Uri(options.BaseAddress.TrimEnd('/') + '/', UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(options.RowsParserRequestTimeoutSeconds);
});
builder.Services.AddScoped<IStructuredTextProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<OllamaStructuredTextProvider>());
builder.Services.AddScoped<IStructuredTextProviderResolver, StructuredTextProviderResolver>();
builder.Services.AddScoped<IReceiptOcrRowReader, AppleReceiptGeometryReader>();
builder.Services.AddScoped<IReceiptRowsAnalysisWorkflow, ReceiptRowsAnalysisWorkflow>();
builder.Services.AddScoped<AppleReceiptExtractor>();
builder.Services.AddScoped<OllamaReceiptExtractor>();
builder.Services.AddScoped<OpenAiReceiptExtractor>();
builder.Services.AddScoped<AnthropicReceiptExtractor>();
builder.Services.AddScoped<AppleGeometryOllamaExtractor>();
builder.Services.AddScoped<IReceiptRecognitionPipeline>(serviceProvider =>
    serviceProvider.GetRequiredService<AppleReceiptExtractor>());
builder.Services.AddScoped<IReceiptRecognitionPipeline>(serviceProvider =>
    serviceProvider.GetRequiredService<OllamaReceiptExtractor>());
builder.Services.AddScoped<IReceiptRecognitionPipeline>(serviceProvider =>
    serviceProvider.GetRequiredService<OpenAiReceiptExtractor>());
builder.Services.AddScoped<IReceiptRecognitionPipeline>(serviceProvider =>
    serviceProvider.GetRequiredService<AnthropicReceiptExtractor>());
builder.Services.AddScoped<IReceiptRecognitionPipeline>(serviceProvider =>
    serviceProvider.GetRequiredService<AppleGeometryOllamaExtractor>());
builder.Services.AddScoped<ReceiptExtractorRouter>();
builder.Services.AddScoped<IReceiptExtractor>(serviceProvider =>
    serviceProvider.GetRequiredService<ReceiptExtractorRouter>());
builder.Services.AddScoped<IReceiptRecognitionStatusProvider>(serviceProvider =>
    serviceProvider.GetRequiredService<ReceiptExtractorRouter>());
builder.Services.AddHostedService<ReceiptProcessingWorker>();

builder.Services.AddSerilog(configuration => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    // IHttpClientFactory logs every request at Information and a failed one with
    // its full stack trace. The Ollama providers already report their own
    // failures in one line (or turn them into a status message), so the factory's
    // per-request lines are noise.
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "PurchaseMemory")
    .WriteTo.Console(
        formatProvider: CultureInfo.InvariantCulture,
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        runtimePaths.LogFilePattern,
        formatProvider: CultureInfo.InvariantCulture,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true,
        flushToDiskInterval: TimeSpan.FromSeconds(1),
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}"));

var mcpJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    // The MCP SDK freezes these options while reflecting tool contracts. .NET 10
    // requires an explicit resolver before custom options can become read-only.
    TypeInfoResolver = new DefaultJsonTypeInfoResolver(),

    // Enums cross the wire as camelCase strings, matching how the REST layer
    // maps them by hand. A numeric enum silently changes meaning as soon as a
    // value is inserted anywhere but the end.
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
};

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "purchase-memory",
            Version = "0.1.0"
        };

        options.ServerInstructions =
            "Purchase Memory stores receipt drafts. Never invent unreadable receipt data: use null. " +
            "Use create_purchase_draft only after an explicit save request, update_purchase_draft for corrections, " +
            "search_purchases for retrieval, search_products, list_product_categories, list_brands or list_tags for the catalog, " +
            "and run_analytics_plan for totals, prices and trends computed by the server, list_review_queue for what still needs checking. Keep item names in the receipt's original language.";
    })
    .WithHttpTransport(options => options.Stateless = true)
    .WithRequestFilters(filters =>
        filters.AddCallToolFilter(McpExpectedErrorFilter.Create()))
    .WithTools<PurchaseTools>(mcpJsonOptions)
    .WithTools<ProductTools>(mcpJsonOptions)
    .WithTools<AnalyticsTools>(mcpJsonOptions)
    .WithTools<ReviewQueueTools>(mcpJsonOptions)
    .WithTools<ImpressionTools>(mcpJsonOptions);

var app = builder.Build();

await app.Services.EnsureDatabaseMigratedAsync();

app.UseReverseProxySupport();
app.UseExceptionHandler();
app.UseSerilogRequestLogging();
// The SPA shell and its assets are public: they hold no data, and the login
// screen is part of them. Everything mapped below is protected by the
// "approved user" fallback policy unless it opts out explicitly.
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Liveness only: anonymous callers learn nothing beyond "the process is up".
app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

app.MapAuthEndpoints();
app.MapMcp("/mcp");
app.MapReceiptEndpoints();
app.MapPurchaseEndpoints();
app.MapProductEndpoints();
app.MapCategoryEndpoints();
app.MapTagEndpoints();
app.MapAnalyticsEndpoints();
app.MapReviewQueueEndpoints();
app.MapImpressionEndpoints();
// Unknown API and MCP paths are real 404s, never the SPA page. Literal
// prefixes outrank the catch-all below, and real endpoints outrank both.
app.MapFallback("/api/{**path}", () => Results.NotFound()).AllowAnonymous();
app.MapFallback("/mcp/{**path}", () => Results.NotFound()).AllowAnonymous();
app.MapFallback("/mcp", () => Results.NotFound()).AllowAnonymous();
app.MapFallbackToFile("index.html").AllowAnonymous();

app.LogAuthMode();

var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
var databaseDescription = databaseSettings.Describe();
var imageStorageDescription = StorageSetup.DescribeImageStorage(builder.Configuration, runtimePaths);
PurchaseMemoryLogMessages.ApplicationStarted(
    startupLogger,
    databaseDescription,
    imageStorageDescription,
    runtimePaths.LogFilePattern);

await app.RunAsync();

public partial class Program;

using Azure.Monitor.OpenTelemetry.Exporter;
using CloudCost.Anomaly.Core;
using CloudCost.Anomaly.Core.Publishing;
using CloudCost.Anomaly.Worker;
using CloudCost.Anomaly.Worker.Services;
using CloudCost.Anomaly.Worker.Services.Publishing;
using CloudCost.Anomaly.Worker.Services.Publishing.Sinks;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
var identifierPattern = "^[A-Za-z][A-Za-z0-9_]*$";

builder.Services.AddOptions<AnomalyWorkerOptions>()
    .Bind(builder.Configuration.GetSection("AnomalyWorker"))
    .Validate(options => options.PollIntervalMinutes >= 5, "PollIntervalMinutes must be at least 5.")
    .Validate(options => options.LookbackDays >= 8, "LookbackDays must be at least 8 so the fallback ranker has enough history.")
    .Validate(options => options.AlertMinimumCostDelta >= 0, "AlertMinimumCostDelta must be non-negative.")
    .Validate(options => System.Text.RegularExpressions.Regex.IsMatch(options.CostTable, identifierPattern), "CostTable must be a valid ADX table identifier.")
    .Validate(options => System.Text.RegularExpressions.Regex.IsMatch(options.AdxDatabase, identifierPattern), "AdxDatabase must be a valid ADX database identifier.")
    .ValidateOnStart();
builder.Services.Configure<OwnershipPolicyOptions>(builder.Configuration.GetSection("OwnershipPolicies"));

builder.Services.AddOptions<EventPublishingOptions>()
    .Bind(builder.Configuration.GetSection("EventPublishing"))
    .Validate(options => options.ChannelCapacity > 0, "ChannelCapacity must be positive.")
    .Validate(options => options.MaxConcurrentDeliveries > 0, "MaxConcurrentDeliveries must be positive.")
    .Validate(options => options.IdempotencyRetentionMinutes > 0, "IdempotencyRetentionMinutes must be positive.")
    .Validate(options => options.EnabledSinks.Count > 0, "At least one sink must be enabled in EnabledSinks.")
    .Validate(
        options => string.IsNullOrWhiteSpace(options.Webhook.Url) || Uri.TryCreate(options.Webhook.Url, UriKind.Absolute, out _),
        "Webhook.Url must be empty or an absolute URI.")
    .Validate(options => System.Text.RegularExpressions.Regex.IsMatch(options.Adx.Table, identifierPattern), "Adx.Table must be a valid ADX table identifier.")
    .ValidateOnStart();

builder.Services.AddSingleton<ChargebackEngine>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OwnershipPolicyOptions>>().Value;
    return new ChargebackEngine(options.Teams);
});
builder.Services.AddSingleton<CostAnomalyRanker>();
builder.Services.AddSingleton<DataGovernancePolicy>();
builder.Services.AddSingleton<TerraformTagScanner>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<OwnershipPolicyOptions>>().Value;
    return new TerraformTagScanner(options.Teams);
});

builder.Services.AddHttpClient<ICostManagementClient, CostManagementClient>(client => client.BaseAddress = new Uri("https://management.azure.com"));
builder.Services.AddSingleton<IAdxCostRepository, AdxCostRepository>();
builder.Services.AddSingleton<WorkerHealthState>();

// Fan-out publishing: bounded channel -> schema-compat + idempotency-checked delivery -> N sinks.
// New destinations are onboarded by adding an IEventSink and listing its name in
// EventPublishing:EnabledSinks; nothing below branches on which sinks exist.
builder.Services.AddSingleton(_ => EventSchemaCatalog.CreateDefault());
builder.Services.AddSingleton<IdempotencyStore>();
builder.Services.AddSingleton<EventPublishingChannel>();
builder.Services.AddSingleton<EventSinkRegistry>();
builder.Services.AddSingleton<IEventSink, AdxEventSink>();
builder.Services.AddHttpClient<WebhookEventSink>();
builder.Services.AddSingleton<IEventSink>(sp => sp.GetRequiredService<WebhookEventSink>());
builder.Services.AddSingleton<IEventSink, FileArchiveEventSink>();
builder.Services.AddHostedService<EventPublishingService>();

builder.Services.AddHostedService<CostAnomalyWorker>();
builder.Services.AddHealthChecks().AddCheck<WorkerHealthCheck>("worker");

var applicationInsightsConnectionString = builder.Configuration["Observability:ApplicationInsightsConnectionString"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("cloud-cost-anomaly-worker"))
    .WithTracing(tracing =>
    {
        tracing
            .AddSource("CloudCost.Anomaly")
            .AddHttpClientInstrumentation()
            .AddOtlpExporter();

        if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
        {
            tracing.AddAzureMonitorTraceExporter(options =>
            {
                options.ConnectionString = applicationInsightsConnectionString;
            });
        }
    });

builder.Logging.ClearProviders();
builder.Logging.AddSerilog(new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger());

var host = builder.Build();
host.Run();

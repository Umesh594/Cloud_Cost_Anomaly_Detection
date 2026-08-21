using Azure.Monitor.OpenTelemetry.Exporter;
using CloudCost.Anomaly.Core;
using CloudCost.Anomaly.Worker;
using CloudCost.Anomaly.Worker.Services;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<AnomalyWorkerOptions>()
    .Bind(builder.Configuration.GetSection("AnomalyWorker"))
    .Validate(options => options.PollIntervalMinutes >= 5, "PollIntervalMinutes must be at least 5.")
    .Validate(options => options.LookbackDays >= 8, "LookbackDays must be at least 8 so the fallback ranker has enough history.")
    .Validate(options => options.AlertMinimumCostDelta >= 0, "AlertMinimumCostDelta must be non-negative.")
    .Validate(options => System.Text.RegularExpressions.Regex.IsMatch(options.CostTable, "^[A-Za-z][A-Za-z0-9_]*$"), "CostTable must be a valid ADX table identifier.")
    .Validate(options => System.Text.RegularExpressions.Regex.IsMatch(options.AdxDatabase, "^[A-Za-z][A-Za-z0-9_]*$"), "AdxDatabase must be a valid ADX database identifier.")
    .Validate(options => Uri.TryCreate(options.AlertWebhookUrl, UriKind.Absolute, out _) || string.IsNullOrWhiteSpace(options.AlertWebhookUrl), "AlertWebhookUrl must be empty or an absolute URI.")
    .ValidateOnStart();
builder.Services.Configure<OwnershipPolicyOptions>(builder.Configuration.GetSection("OwnershipPolicies"));

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
builder.Services.AddHttpClient<IAlertPublisher, AlertPublisher>();
builder.Services.AddSingleton<WorkerHealthState>();
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

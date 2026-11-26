using System.Data.Common;
using Backend.Kernel;
using Backend.Kernel.Behaviors;
using Backend.Modules.Home;
using Mediator;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Observability: traces, metrics and logs over OTLP (AD: Observability). Endpoint, protocol and
// service name come from the standard OTEL_* environment variables set in docker-compose.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? "smart-home-backend"))
    .WithTracing(tracing => tracing
        .AddSource(Mediator.Mediator.ActivitySourceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter(Mediator.Mediator.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation())
    .WithLogging(configureBuilder: null, configureOptions: options =>
    {
        options.IncludeFormattedMessage = true;
        options.IncludeScopes = true;
    })
    .UseOtlpExporter();

// Kernel: own mediator with the pipeline Validation, then Transaction (AD-5), and domain event dispatching (AD-6).
// All module DbContexts of a scope share this one connection (AD-14).
builder.Services.AddScoped<DbConnection>(_ => new NpgsqlConnection(builder.Configuration.GetConnectionString("Default")));
// Mediator (source generator). Scoped: every entry point has its own DI scope (AD-15). The pipeline is
// Validation, then Transaction (AD-5). The generated types stay internal so the test project can have its own.
builder.Services.AddMediator(options =>
{
    options.ServiceLifetime = ServiceLifetime.Scoped;
    options.GenerateTypesAsInternal = true;
    options.PipelineBehaviors = [typeof(ValidationBehavior<,>), typeof(TransactionBehavior<,>)];
    options.Telemetry.EnableTracing = true;
    options.Telemetry.EnableMetrics = true;
});
builder.Services.AddKernel();

// Home module: Home Assistant gateway (AD-18), a background service. Config: HomeAssistant:Url, HomeAssistant:Token, HomeAssistant:Entities.
builder.Services.AddHome(builder.Configuration);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

await app.Services.MigrateHomeAsync(CancellationToken.None);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.Run();

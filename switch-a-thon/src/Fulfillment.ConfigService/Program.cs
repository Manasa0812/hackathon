using Fulfillment.ConfigService.Data;
using Fulfillment.ConfigService.Contracts;
using Fulfillment.ConfigService.Services;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("ConfigurationDatabase")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__ConfigurationDatabase")
    ?? "Host=localhost;Port=5432;Database=resolveiq;Username=resolveiq;Password=resolveiq_dev";

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<ConfigurationDatabase>();
builder.Services.AddSingleton<PackageRepository>();
builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddScoped<ZonesService>();
builder.Services.AddScoped<RatesService>();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(entry => entry.Key,
                entry => entry.Value!.Errors.Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "The supplied value is invalid."
                    : error.ErrorMessage).ToArray());
        var response = new ApiErrorResponse("VALIDATION_ERROR", "One or more required fields are missing or invalid.",
            errors, System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier);
        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("RequestValidation")
            .LogWarning("Configuration request validation failed for fields {Fields}; trace {CorrelationId}.",
                errors.Keys, response.CorrelationId);
        return new BadRequestObjectResult(response);
    };
});
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Fulfillment.ConfigService"))
    .WithMetrics(metrics => metrics
        .AddMeter(Fulfillment.ConfigService.ConfigurationMetrics.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter())
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());
builder.Logging.AddOpenTelemetry(logging =>
{
    logging.IncludeFormattedMessage = true;
    logging.IncludeScopes = true;
    logging.AddOtlpExporter();
});
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:Configuration"]
        ?? Environment.GetEnvironmentVariable("Redis__Configuration")
        ?? "localhost:6379";
    options.InstanceName = "resolveiq:config:";
});

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapControllers();
app.Run();

using Fulfillment.LabelService.Contracts;
using Fulfillment.LabelService.Services;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;

var builder = WebApplication.CreateBuilder(args);
var configurationBaseUrl = builder.Configuration["ConfigurationService:BaseUrl"]
    ?? Environment.GetEnvironmentVariable("ConfigurationService__BaseUrl")
    ?? "http://localhost:5081/";

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
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
        var correlationId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("RequestValidation")
            .LogWarning("Label request validation failed for fields {Fields}; trace {CorrelationId}.", errors.Keys, correlationId);
        return new BadRequestObjectResult(new LabelErrorResponse("INVALID_SHIPMENT_DETAILS",
            "One or more required shipment fields are missing or invalid.",
            correlationId,
            errors));
    };
});
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("Fulfillment.LabelService"))
    .WithMetrics(metrics => metrics
        .AddMeter(Fulfillment.LabelService.DiagnosticsMetrics.MeterName)
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
builder.Services.AddHttpClient<ConfigurationClient>(client =>
{
    client.BaseAddress = new Uri(configurationBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(4);
});

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapControllers();
app.Run();

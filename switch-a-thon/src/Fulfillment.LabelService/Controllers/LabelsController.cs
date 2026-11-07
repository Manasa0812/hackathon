using Fulfillment.LabelService.Contracts;
using Fulfillment.LabelService.Services;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Fulfillment.LabelService.Controllers;

[ApiController]
[Route("api/labels")]
public sealed class LabelsController(ConfigurationClient configurationClient, ILogger<LabelsController> logger) : ControllerBase
{
    [HttpPost("/labels")]
    [ProducesResponseType<CreateLabelResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<LabelErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<LabelErrorResponse>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<LabelErrorResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CreateLabelResponse>> Create([FromBody] CreateLabelRequest request, CancellationToken cancellationToken)
    {
        var correlationId = Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier;
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["CarrierServiceType"] = request.CarrierServiceType
        });
        try
        {
            logger.LogInformation("Starting simulated label flow for destination {DestinationCountry}/{DestinationPostcode}; trace {CorrelationId}.",
                request.DestinationCountry, request.DestinationPostcode, correlationId);
            var zone = await configurationClient.GetZoneAsync(request, cancellationToken);
            var rate = await configurationClient.GetRateAsync(request, zone.Zone, cancellationToken);
            var response = new CreateLabelResponse("Generated", $"SIM-LBL-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
                rate.CarrierServiceType, rate.Zone, rate.Rate, rate.Currency,
                "Simulated label workflow completed successfully. No carrier integration or label document was created.",
                correlationId);
            DiagnosticsMetrics.RecordLabelAttempt("generated", null, StatusCodes.Status200OK);
            logger.LogInformation("Simulated label flow succeeded for zone {Zone} at rate {Rate} {Currency}; trace {CorrelationId}.",
                rate.Zone, rate.Rate, rate.Currency, correlationId);
            return Ok(response);
        }
        catch (ConfigurationServiceException exception)
        {
            var (status, code, message) = MapConfigurationError(exception.Code);
            DiagnosticsMetrics.RecordLabelAttempt("failed", exception.Code, status);
            logger.LogWarning("Configuration lookup failed with {InternalErrorCode}; returned {ConsumerErrorCode}; trace {CorrelationId}.",
                exception.Code, code, correlationId);
            return StatusCode(status, new LabelErrorResponse(code, message, correlationId));
        }
        catch (HttpRequestException exception)
        {
            DiagnosticsMetrics.RecordLabelAttempt("failed", "CONFIGURATION_UNAVAILABLE", StatusCodes.Status503ServiceUnavailable);
            logger.LogError(exception, "Configuration Service was unreachable; trace {CorrelationId}.", correlationId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new LabelErrorResponse("SHIPPING_CONFIGURATION_UNAVAILABLE", "Shipping options are temporarily unavailable. Retry shortly.", correlationId));
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            DiagnosticsMetrics.RecordLabelAttempt("failed", "CONFIGURATION_TIMEOUT", StatusCodes.Status503ServiceUnavailable);
            logger.LogError(exception, "Configuration lookup timed out; trace {CorrelationId}.", correlationId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new LabelErrorResponse("SHIPPING_CONFIGURATION_TIMEOUT", "Shipping options took too long to load. Retry shortly.", correlationId));
        }
    }

    private static (int Status, string Code, string Message) MapConfigurationError(string code) => code switch
    {
        "ZONE_NOT_FOUND" => (StatusCodes.Status422UnprocessableEntity, "DESTINATION_NOT_SERVICEABLE", "No shipping zone is configured for these addresses and service."),
        "ZONE_RATE_NOT_FOUND" => (StatusCodes.Status422UnprocessableEntity, "SERVICE_RATE_UNAVAILABLE", "No rate is configured for this service in the matched zone."),
        "RATE_NOT_FOUND" => (StatusCodes.Status422UnprocessableEntity, "PARCEL_OUTSIDE_SERVICE_LIMITS", "No rate supports the supplied parcel weight and dimensions."),
        "VALIDATION_ERROR" => (StatusCodes.Status400BadRequest, "INVALID_SHIPMENT_DETAILS", "The shipment details could not be processed."),
        _ => (StatusCodes.Status502BadGateway, "SHIPPING_CONFIGURATION_ERROR", "Shipping options could not be resolved.")
    };
}

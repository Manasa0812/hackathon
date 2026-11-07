namespace Fulfillment.LabelService.Contracts;

public sealed record ZoneLookupResponse(string CarrierServiceType, string Zone);
public sealed record RateLookupResponse(string CarrierServiceType, string Zone, decimal Rate, string Currency);

public sealed record CreateLabelResponse(
    string Status,
    string LabelReference,
    string CarrierServiceType,
    string Zone,
    decimal Rate,
    string Currency,
    string Message,
    string CorrelationId);

public sealed record LabelErrorResponse(
    string Code,
    string Message,
    string CorrelationId,
    IReadOnlyDictionary<string, string[]>? Errors = null);

public sealed record ConfigurationErrorResponse(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? Errors,
    string? CorrelationId);

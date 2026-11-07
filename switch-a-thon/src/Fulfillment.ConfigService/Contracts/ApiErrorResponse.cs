namespace Fulfillment.ConfigService.Contracts;

public sealed record ApiErrorResponse(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? Errors,
    string? CorrelationId);

public sealed record LookupResult<T>(T? Value, string? ErrorCode, string? Message) where T : class
{
    public bool Succeeded => Value is not null;
}

using System.ComponentModel.DataAnnotations;

namespace Fulfillment.ConfigService.Contracts;

public sealed record RateLookupRequest(
    [param: Required, StringLength(40, MinimumLength = 1)] string CarrierServiceType,
    [param: Required, StringLength(80, MinimumLength = 1)] string Zone,
    [param: Range(typeof(decimal), "0.01", "1000")] decimal Weight,
    [param: Range(typeof(decimal), "0.01", "10000")] decimal Length,
    [param: Range(typeof(decimal), "0.01", "10000")] decimal Height,
    [param: Range(typeof(decimal), "0.01", "10000")] decimal Width);

public sealed record RateLookupResponse(string CarrierServiceType, string Zone, decimal Rate, string Currency);

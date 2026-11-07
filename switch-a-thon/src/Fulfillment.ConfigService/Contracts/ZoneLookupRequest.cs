using System.ComponentModel.DataAnnotations;

namespace Fulfillment.ConfigService.Contracts;

public sealed record ZoneLookupRequest(
    [param: Required, StringLength(40, MinimumLength = 1)] string CarrierServiceType,
    [param: Required, StringLength(16, MinimumLength = 1)] string OriginPostcode,
    [param: Required, StringLength(16, MinimumLength = 1)] string DestinationPostcode,
    [param: Required, RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a two-letter origin country code.")] string OriginCountry,
    [param: Required, RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a two-letter destination country code.")] string DestinationCountry);

public sealed record ZoneLookupResponse(string CarrierServiceType, string Zone);

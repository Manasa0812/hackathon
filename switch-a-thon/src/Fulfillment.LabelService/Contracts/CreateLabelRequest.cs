using System.ComponentModel.DataAnnotations;

namespace Fulfillment.LabelService.Contracts;

public sealed record CreateLabelRequest(
    [param: Required, StringLength(40, MinimumLength = 1)] string CarrierServiceType,
    [param: Required, StringLength(16, MinimumLength = 1)] string OriginPostcode,
    [param: Required, StringLength(16, MinimumLength = 1)] string DestinationPostcode,
    [param: Required, RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a two-letter origin country code.")] string OriginCountry,
    [param: Required, RegularExpression("^[A-Za-z]{2}$", ErrorMessage = "Use a two-letter destination country code.")] string DestinationCountry,
    [param: Range(typeof(decimal), "0.01", "1000")] decimal Weight,
    [param: Range(typeof(decimal), "0.01", "10000")] decimal Length,
    [param: Range(typeof(decimal), "0.01", "10000")] decimal Height,
    [param: Range(typeof(decimal), "0.01", "10000")] decimal Width);

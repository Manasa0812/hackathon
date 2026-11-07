using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Fulfillment.LabelService.Contracts;

namespace Fulfillment.LabelService.Services;

public sealed class ConfigurationServiceException(HttpStatusCode statusCode, string code, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

public sealed class ConfigurationClient(HttpClient httpClient)
{
    public Task<ZoneLookupResponse> GetZoneAsync(CreateLabelRequest request, CancellationToken cancellationToken) =>
        GetAsync<ZoneLookupResponse>("zones", new Dictionary<string, string>
        {
            ["carrierServiceType"] = request.CarrierServiceType,
            ["originPostcode"] = request.OriginPostcode,
            ["destinationPostcode"] = request.DestinationPostcode,
            ["originCountry"] = request.OriginCountry,
            ["destinationCountry"] = request.DestinationCountry
        }, cancellationToken);

    public Task<RateLookupResponse> GetRateAsync(CreateLabelRequest request, string zone, CancellationToken cancellationToken) =>
        GetAsync<RateLookupResponse>("rates", new Dictionary<string, string>
        {
            ["carrierServiceType"] = request.CarrierServiceType,
            ["zone"] = zone,
            ["weight"] = request.Weight.ToString(CultureInfo.InvariantCulture),
            ["length"] = request.Length.ToString(CultureInfo.InvariantCulture),
            ["height"] = request.Height.ToString(CultureInfo.InvariantCulture),
            ["width"] = request.Width.ToString(CultureInfo.InvariantCulture)
        }, cancellationToken);

    private async Task<T> GetAsync<T>(string path, IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        var query = string.Join("&", parameters.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        using var response = await httpClient.GetAsync($"{path}?{query}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadFromJsonAsync<ConfigurationErrorResponse>(cancellationToken);
            throw new ConfigurationServiceException(response.StatusCode,
                error?.Code ?? "CONFIGURATION_ERROR", error?.Message ?? "Configuration lookup failed.");
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
            ?? throw new InvalidOperationException("Configuration Service returned an empty response.");
    }
}

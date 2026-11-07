using System.Text.Json;
using Fulfillment.ConfigService.Contracts;
using Fulfillment.ConfigService.Data;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace Fulfillment.ConfigService.Services;

public sealed class ZonesService(
    PackageRepository repository,
    IDistributedCache cache,
    ILogger<ZonesService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LookupResult<ZoneLookupResponse>> GetZoneAsync(ZoneLookupRequest request, CancellationToken cancellationToken)
    {
        var normalized = request with
        {
            CarrierServiceType = request.CarrierServiceType.Trim().ToUpperInvariant(),
            OriginPostcode = request.OriginPostcode.Trim().ToUpperInvariant(),
            DestinationPostcode = request.DestinationPostcode.Trim().ToUpperInvariant(),
            OriginCountry = request.OriginCountry.Trim().ToUpperInvariant(),
            DestinationCountry = request.DestinationCountry.Trim().ToUpperInvariant()
        };
        var key = $"package:zone:{normalized.CarrierServiceType}:{normalized.OriginCountry}:{normalized.OriginPostcode}:{normalized.DestinationCountry}:{normalized.DestinationPostcode}";
        var cached = await TryGetCachedAsync(key, cancellationToken);
        if (cached is not null)
        {
            RecordFailureIfNeeded(cached, normalized);
            return cached;
        }

        var zone = await repository.FindZoneAsync(normalized, cancellationToken);
        var result = zone is null
            ? new LookupResult<ZoneLookupResponse>(null, "ZONE_NOT_FOUND", "No shipping zone matches the supplied carrier service and postal addresses.")
            : new LookupResult<ZoneLookupResponse>(zone, null, null);
        RecordFailureIfNeeded(result, normalized);
        await TrySetCachedAsync(key, result, cancellationToken);
        return result;
    }

    private async Task<LookupResult<ZoneLookupResponse>?> TryGetCachedAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var json = await cache.GetStringAsync(key, cancellationToken);
            return json is null ? null : JsonSerializer.Deserialize<LookupResult<ZoneLookupResponse>>(json, JsonOptions);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Zone cache read failed; checking PostgreSQL.");
            return null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Zone cache entry was invalid; checking PostgreSQL.");
            return null;
        }
    }

    private async Task TrySetCachedAsync(string key, LookupResult<ZoneLookupResponse> result, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetStringAsync(key, JsonSerializer.Serialize(result, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }, cancellationToken);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Zone cache write failed; returning the PostgreSQL result.");
        }
    }

    private void RecordFailureIfNeeded(LookupResult<ZoneLookupResponse> result, ZoneLookupRequest request)
    {
        if (result.Succeeded) return;
        Fulfillment.ConfigService.ConfigurationMetrics.RecordFailure("zone", result.ErrorCode!);
        logger.LogWarning("Zone lookup failed with {ErrorCode} for service {CarrierServiceType}, origin {OriginCountry}/{OriginPostcode}, destination {DestinationCountry}/{DestinationPostcode}; trace {TraceId}.",
            result.ErrorCode, request.CarrierServiceType, request.OriginCountry, request.OriginPostcode,
            request.DestinationCountry, request.DestinationPostcode, System.Diagnostics.Activity.Current?.TraceId.ToString());
    }
}

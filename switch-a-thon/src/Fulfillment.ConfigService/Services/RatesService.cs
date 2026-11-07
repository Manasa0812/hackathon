using System.Text.Json;
using System.Globalization;
using Fulfillment.ConfigService.Contracts;
using Fulfillment.ConfigService.Data;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace Fulfillment.ConfigService.Services;

public sealed class RatesService(
    PackageRepository repository,
    IDistributedCache cache,
    ILogger<RatesService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LookupResult<RateLookupResponse>> GetRateAsync(RateLookupRequest request, CancellationToken cancellationToken)
    {
        var normalized = request with
        {
            CarrierServiceType = request.CarrierServiceType.Trim().ToUpperInvariant(),
            Zone = request.Zone.Trim().ToUpperInvariant()
        };
        var key = string.Join(":", "package", "rate", normalized.CarrierServiceType, normalized.Zone,
            normalized.Weight.ToString(CultureInfo.InvariantCulture), normalized.Length.ToString(CultureInfo.InvariantCulture),
            normalized.Height.ToString(CultureInfo.InvariantCulture), normalized.Width.ToString(CultureInfo.InvariantCulture));
        var cached = await TryGetCachedAsync(key, cancellationToken);
        if (cached is not null)
        {
            RecordFailureIfNeeded(cached, normalized);
            return cached;
        }

        LookupResult<RateLookupResponse> result;
        if (!await repository.HasRatesAsync(normalized.CarrierServiceType, normalized.Zone, cancellationToken))
        {
            result = new LookupResult<RateLookupResponse>(null, "ZONE_RATE_NOT_FOUND",
                $"No rates are configured for service '{normalized.CarrierServiceType}' in zone '{normalized.Zone}'.");
        }
        else
        {
            var rate = await repository.FindMatchingRateAsync(normalized, cancellationToken);
            result = rate is null
                ? new LookupResult<RateLookupResponse>(null, "RATE_NOT_FOUND",
                    $"No configured rate supports the supplied weight and dimensions in zone '{normalized.Zone}'.")
                : new LookupResult<RateLookupResponse>(rate, null, null);
        }
        RecordFailureIfNeeded(result, normalized);
        await TrySetCachedAsync(key, result, cancellationToken);
        return result;
    }

    private async Task<LookupResult<RateLookupResponse>?> TryGetCachedAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var json = await cache.GetStringAsync(key, cancellationToken);
            return json is null ? null : JsonSerializer.Deserialize<LookupResult<RateLookupResponse>>(json, JsonOptions);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Rate cache read failed; checking PostgreSQL.");
            return null;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Rate cache entry was invalid; checking PostgreSQL.");
            return null;
        }
    }

    private async Task TrySetCachedAsync(string key, LookupResult<RateLookupResponse> result, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetStringAsync(key, JsonSerializer.Serialize(result, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) }, cancellationToken);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Rate cache write failed; returning the PostgreSQL result.");
        }
    }

    private void RecordFailureIfNeeded(LookupResult<RateLookupResponse> result, RateLookupRequest request)
    {
        if (result.Succeeded) return;
        Fulfillment.ConfigService.ConfigurationMetrics.RecordFailure("rate", result.ErrorCode!);
        logger.LogWarning("Rate lookup failed with {ErrorCode} for service {CarrierServiceType}, zone {Zone}, weight {Weight}, dimensions {Length}x{Height}x{Width}; trace {TraceId}.",
            result.ErrorCode, request.CarrierServiceType, request.Zone, request.Weight,
            request.Length, request.Height, request.Width, System.Diagnostics.Activity.Current?.TraceId.ToString());
    }
}

using Fulfillment.ConfigService.Contracts;
using Npgsql;

namespace Fulfillment.ConfigService.Data;

public sealed class PackageRepository(NpgsqlDataSource dataSource)
{
    public async Task<ZoneLookupResponse?> FindZoneAsync(ZoneLookupRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT zone_code
            FROM package_zones
            WHERE active
              AND carrier_service_type = @service
              AND origin_country = @originCountry
              AND @originPostcode LIKE origin_postcode_prefix || '%'
              AND destination_country = @destinationCountry
              AND @destinationPostcode LIKE destination_postcode_prefix || '%'
            ORDER BY length(origin_postcode_prefix) + length(destination_postcode_prefix) DESC
            LIMIT 1
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("service", Normalize(request.CarrierServiceType));
        command.Parameters.AddWithValue("originCountry", Normalize(request.OriginCountry));
        command.Parameters.AddWithValue("originPostcode", Normalize(request.OriginPostcode));
        command.Parameters.AddWithValue("destinationCountry", Normalize(request.DestinationCountry));
        command.Parameters.AddWithValue("destinationPostcode", Normalize(request.DestinationPostcode));
        var zone = await command.ExecuteScalarAsync(cancellationToken) as string;
        return zone is null ? null : new ZoneLookupResponse(Normalize(request.CarrierServiceType), zone);
    }

    public async Task<bool> HasRatesAsync(string service, string zone, CancellationToken cancellationToken)
    {
        const string configuredSql = """
            SELECT EXISTS (
                SELECT 1 FROM package_rates
                WHERE active AND carrier_service_type = @service AND zone_code = @zone)
            """;
        await using var configuredCommand = dataSource.CreateCommand(configuredSql);
        configuredCommand.Parameters.AddWithValue("service", Normalize(service));
        configuredCommand.Parameters.AddWithValue("zone", Normalize(zone));
        return (bool)(await configuredCommand.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task<RateLookupResponse?> FindMatchingRateAsync(RateLookupRequest request, CancellationToken cancellationToken)
    {
        const string matchingSql = """
            SELECT rate, currency
            FROM package_rates
            WHERE active
              AND carrier_service_type = @service
              AND zone_code = @zone
              AND @weight BETWEEN min_weight AND max_weight
              AND @length <= max_length
              AND @height <= max_height
              AND @width <= max_width
            ORDER BY rate ASC
            LIMIT 1
            """;
        await using var command = dataSource.CreateCommand(matchingSql);
        command.Parameters.AddWithValue("service", Normalize(request.CarrierServiceType));
        command.Parameters.AddWithValue("zone", Normalize(request.Zone));
        command.Parameters.AddWithValue("weight", request.Weight);
        command.Parameters.AddWithValue("length", request.Length);
        command.Parameters.AddWithValue("height", request.Height);
        command.Parameters.AddWithValue("width", request.Width);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new RateLookupResponse(Normalize(request.CarrierServiceType), Normalize(request.Zone),
            reader.GetDecimal(0), reader.GetString(1).Trim());
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}

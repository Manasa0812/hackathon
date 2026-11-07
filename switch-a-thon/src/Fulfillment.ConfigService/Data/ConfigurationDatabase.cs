using Npgsql;

namespace Fulfillment.ConfigService.Data;

public sealed class ConfigurationDatabase(NpgsqlDataSource dataSource)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS package_zones (
                id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                carrier_service_type TEXT NOT NULL,
                origin_country CHAR(2) NOT NULL,
                origin_postcode_prefix TEXT NOT NULL,
                destination_country CHAR(2) NOT NULL,
                destination_postcode_prefix TEXT NOT NULL,
                zone_code TEXT NOT NULL,
                active BOOLEAN NOT NULL DEFAULT TRUE,
                UNIQUE (carrier_service_type, origin_country, origin_postcode_prefix,
                        destination_country, destination_postcode_prefix)
            );
            CREATE TABLE IF NOT EXISTS package_rates (
                id BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                carrier_service_type TEXT NOT NULL,
                zone_code TEXT NOT NULL,
                min_weight NUMERIC(8, 2) NOT NULL CHECK (min_weight > 0),
                max_weight NUMERIC(8, 2) NOT NULL CHECK (max_weight >= min_weight),
                max_length NUMERIC(8, 2) NOT NULL CHECK (max_length > 0),
                max_height NUMERIC(8, 2) NOT NULL CHECK (max_height > 0),
                max_width NUMERIC(8, 2) NOT NULL CHECK (max_width > 0),
                rate NUMERIC(10, 2) NOT NULL CHECK (rate >= 0),
                currency CHAR(3) NOT NULL DEFAULT 'USD',
                active BOOLEAN NOT NULL DEFAULT TRUE,
                UNIQUE (carrier_service_type, zone_code, max_weight, max_length, max_height, max_width)
            );
            INSERT INTO package_zones
                (carrier_service_type, origin_country, origin_postcode_prefix,
                 destination_country, destination_postcode_prefix, zone_code)
            VALUES ('GROUND', 'US', '941', 'US', '100', 'US-WEST-TO-NORTHEAST'),
                   ('GROUND', 'US', '941', 'US', '900', 'US-WEST-LOCAL'),
                   ('EXPRESS', 'US', '941', 'US', '100', 'US-WEST-TO-NORTHEAST'),
                   ('EXPRESS', 'US', '941', 'US', '900', 'US-WEST-LOCAL'),
                   ('ECONOMY', 'US', '941', 'US', '100', 'US-WEST-TO-NORTHEAST')
            ON CONFLICT (carrier_service_type, origin_country, origin_postcode_prefix,
                         destination_country, destination_postcode_prefix) DO NOTHING;
            INSERT INTO package_rates
                (carrier_service_type, zone_code, min_weight, max_weight, max_length,
                 max_height, max_width, rate, currency)
            VALUES ('GROUND', 'US-WEST-TO-NORTHEAST', 0.01, 20, 100, 70, 70, 12.75, 'USD'),
                   ('EXPRESS', 'US-WEST-TO-NORTHEAST', 0.01, 10, 100, 70, 70, 22.50, 'USD'),
                   ('GROUND', 'US-WEST-LOCAL', 0.01, 30, 120, 80, 80, 8.50, 'USD'),
                   ('EXPRESS', 'US-WEST-LOCAL', 0.01, 15, 100, 70, 70, 15.25, 'USD')
            ON CONFLICT (carrier_service_type, zone_code, max_weight, max_length, max_height, max_width) DO NOTHING;
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

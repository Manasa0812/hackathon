using System.ComponentModel;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;
using ModelContextProtocol.Server;
using Npgsql;
using StackExchange.Redis;

namespace ResolveIQ.InvestigatorMcp;

[McpServerToolType]
public sealed class InvestigationTools(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    NpgsqlDataSource database,
    IConnectionMultiplexer redis)
{
    private static readonly Regex SecretPattern = new(
        "(?i)(\\b(?:authorization|password|passwd|secret|api[_-]?key|access[_-]?token|refresh[_-]?token)\\b\\s*[:=]\\s*)([^\\s,;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BearerPattern = new("(?i)\\bBearer\\s+[A-Za-z0-9._~+/-]+=*", RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new("\\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [McpServerTool, Description("Resolve a shipment zone and matching package rate through ConfigService. This read-only lookup calls GET /zones followed by GET /rates.")]
    public async Task<string> GetCarrierQuote(
        [Description("Service type, for example GROUND or EXPRESS.")] string carrier_service_type,
        [Description("Origin postal code.")] string origin_postcode,
        [Description("Destination postal code.")] string destination_postcode,
        [Description("Two-letter origin country code.")] string origin_country,
        [Description("Two-letter destination country code.")] string destination_country,
        [Description("Shipment weight in kilograms.")] decimal weight,
        [Description("Parcel length in centimeters.")] decimal length,
        [Description("Parcel height in centimeters.")] decimal height,
        [Description("Parcel width in centimeters.")] decimal width,
        CancellationToken cancellationToken)
    {
        if (new[] { carrier_service_type, origin_postcode, destination_postcode, origin_country, destination_country }
            .Any(string.IsNullOrWhiteSpace)) return "All service and address fields are required.";
        if (weight <= 0 || length <= 0 || height <= 0 || width <= 0)
            return "Weight and all parcel dimensions must be greater than zero.";

        var client = CreateClient("ConfigurationService:BaseUrl", "http://localhost:5081/");
        var zoneQuery = BuildQuery(new Dictionary<string, string>
        {
            ["carrierServiceType"] = carrier_service_type,
            ["originPostcode"] = origin_postcode,
            ["destinationPostcode"] = destination_postcode,
            ["originCountry"] = origin_country,
            ["destinationCountry"] = destination_country
        });
        using var zoneResponse = await client.GetAsync("zones?" + zoneQuery, cancellationToken);
        if (!zoneResponse.IsSuccessStatusCode) return "Zone lookup: " + await ReadResponseAsync(zoneResponse, cancellationToken);
        var zoneContent = await zoneResponse.Content.ReadAsStringAsync(cancellationToken);
        using var zoneDocument = JsonDocument.Parse(zoneContent);
        var zone = zoneDocument.RootElement.GetProperty("zone").GetString();
        if (string.IsNullOrWhiteSpace(zone)) return "Configuration Service returned a zone response without a zone value.";

        var rateQuery = BuildQuery(new Dictionary<string, string>
        {
            ["carrierServiceType"] = carrier_service_type,
            ["zone"] = zone,
            ["weight"] = weight.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["length"] = length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["height"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["width"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
        using var rateResponse = await client.GetAsync("rates?" + rateQuery, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            zone = zoneDocument.RootElement.Clone(),
            rate = await ReadResponseAsync(rateResponse, cancellationToken)
        }, JsonOptions);
    }

    [McpServerTool, Description("Read aggregate PostgreSQL state for the ResolveIQ demo. Only fixed SELECT queries are exposed; order references and item descriptions are not returned.")]
    public async Task<string> GetPostgresSnapshot(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await database.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand("""
                SELECT
                    (SELECT COUNT(*) FROM package_zones WHERE active) AS active_package_zones,
                    (SELECT COUNT(*) FROM package_rates WHERE active) AS active_package_rates
                """, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return "PostgreSQL returned no summary row.";

            var result = new
            {
                database = "resolveiq",
                activePackageZones = reader.GetInt64(0),
                activePackageRates = reader.GetInt64(1)
            };
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (Exception exception) when (IsDependencyException(exception))
        {
            return $"PostgreSQL snapshot unavailable: {SafeMessage(exception)}";
        }
    }

    [McpServerTool, Description("Check Redis connectivity with a read-only PING. Use Prometheus for memory, hit/miss, and eviction metrics.")]
    public async Task<string> GetRedisSnapshot(CancellationToken cancellationToken)
    {
        try
        {
            var database = redis.GetDatabase();
            var ping = await database.PingAsync().WaitAsync(cancellationToken);
            return JsonSerializer.Serialize(new
            {
                endpoint = "configured local Redis",
                reachable = true,
                pingMilliseconds = Math.Round(ping.TotalMilliseconds, 2)
            }, JsonOptions);
        }
        catch (Exception exception) when (IsDependencyException(exception))
        {
            return $"Redis snapshot unavailable: {SafeMessage(exception)}";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return $"Redis snapshot failed: {exception.GetType().Name}";
        }
    }

    [McpServerTool, Description("Query a small allowlist of Prometheus metrics used by Grafana. The lookback is bounded to 60 minutes; arbitrary PromQL is not accepted.")]
    public async Task<string> GetInfrastructureMetrics(
        [Description("Metric view: http_requests, http_latency_p95, redis_memory, redis_cache, postgres_connections, or postgres_database_size.")] string metric_view,
        [Description("Lookback in minutes, from 1 to 60. Use 5 for the usual incident window.")] int lookback_minutes = 5,
        CancellationToken cancellationToken = default)
    {
        if (lookback_minutes is < 1 or > 60) return "lookback_minutes must be between 1 and 60.";
        var range = $"{lookback_minutes}m";
        var query = metric_view.Trim().ToLowerInvariant() switch
        {
            "http_requests" => $"sum by (service_name) (increase(http_server_request_duration_seconds_count[{range}]))",
            "http_latency_p95" => $"histogram_quantile(0.95, sum by (service_name, le) (rate(http_server_request_duration_seconds_bucket[{range}])))",
            "redis_memory" => "redis_memory_used_bytes or redis_memory_used_rss_bytes",
            "redis_cache" => "sum(rate(redis_keyspace_hits_total[" + range + "])) or sum(rate(redis_keyspace_misses_total[" + range + "]))",
            "postgres_connections" => "pg_stat_database_numbackends{datname=\"resolveiq\"}",
            "postgres_database_size" => "pg_database_size_bytes{datname=\"resolveiq\"}",
            _ => null
        };
        if (query is null) return "Unknown metric_view. Choose one of the documented metric views.";

        try
        {
            var baseUrl = configuration["Prometheus:BaseUrl"] ?? "http://localhost:9090/";
            var client = CreateClient("Prometheus:BaseUrl", baseUrl);
            var uri = "api/v1/query?query=" + Uri.EscapeDataString(query);
            using var response = await client.GetAsync(uri, cancellationToken);
            return await ReadResponseAsync(response, cancellationToken);
        }
        catch (Exception exception) when (IsDependencyException(exception))
        {
            return $"Prometheus query unavailable: {SafeMessage(exception)}";
        }
    }

    [McpServerTool, Description("On-demand ResolveIQ error investigation. Counts a known package zone/rate error or HTTP status in Prometheus, then searches both LabelService and ConfigService logs in Loki for the same error.")]
    public async Task<string> InvestigateObservedErrors(
        [Description("A known error code (ZONE_NOT_FOUND, ZONE_RATE_NOT_FOUND, RATE_NOT_FOUND, CONFIGURATION_UNAVAILABLE) or HTTP status (400, 422, 503). Do not pass arbitrary PromQL.")] string error_code_or_status,
        [Description("Lookback in minutes, from 1 to 60.")] int lookback_minutes = 5,
        CancellationToken cancellationToken = default)
    {
        if (lookback_minutes is < 1 or > 60) return "lookback_minutes must be between 1 and 60.";
        var value = error_code_or_status.Trim().ToUpperInvariant();
        var query = value switch
        {
            "ZONE_NOT_FOUND" or "ZONE_RATE_NOT_FOUND" or "RATE_NOT_FOUND" =>
                $"sum(increase(resolveiq_configuration_lookup_failures_total{{error_code=\"{value}\"}}[{lookback_minutes}m]))",
            "CONFIGURATION_UNAVAILABLE" =>
                $"sum(increase(resolveiq_label_attempts_total{{error_code=\"{value}\"}}[{lookback_minutes}m]))",
            "400" or "422" or "503" =>
                $"sum by (service_name, http_response_status_code) (increase(http_server_request_duration_seconds_count{{http_response_status_code=\"{value}\"}}[{lookback_minutes}m]))",
            _ => null
        };
        if (query is null)
            return "Unsupported error selector. Choose ZONE_NOT_FOUND, ZONE_RATE_NOT_FOUND, RATE_NOT_FOUND, CONFIGURATION_UNAVAILABLE, or HTTP status 400, 422, or 503.";

        string metrics;
        try
        {
            var client = CreateClient("Prometheus:BaseUrl", configuration["Prometheus:BaseUrl"] ?? "http://localhost:9090/");
            using var response = await client.GetAsync("api/v1/query?query=" + Uri.EscapeDataString(query), cancellationToken);
            metrics = await ReadResponseAsync(response, cancellationToken);
        }
        catch (Exception exception) when (IsDependencyException(exception))
        {
            metrics = $"Prometheus query unavailable: {SafeMessage(exception)}";
        }

        var logs = await SearchApplicationLogs("all", lookback_minutes, value, cancellationToken);
        return JsonSerializer.Serialize(new
        {
            investigation = value,
            lookbackMinutes = lookback_minutes,
            prometheusQuery = query,
            prometheusEvidence = metrics,
            lokiEvidenceAcrossServices = logs,
            note = "A zero or empty metric result means no matching samples were retained in this window; verify the services have emitted telemetry."
        }, JsonOptions);
    }

    [McpServerTool, Description("Search a bounded window of ResolveIQ application logs in Loki. Only the two demo services may be queried, and results are redacted and limited.")]
    public async Task<string> SearchApplicationLogs(
        [Description("Service name: Fulfillment.LabelService or Fulfillment.ConfigService.")] string service_name,
        [Description("Lookback in minutes, from 1 to 60.")] int lookback_minutes = 15,
        [Description("Optional text to filter log lines by.")] string? contains = null,
        CancellationToken cancellationToken = default)
    {
        if (lookback_minutes is < 1 or > 60) return "lookback_minutes must be between 1 and 60.";
        var service = service_name.Trim() switch
        {
            "Fulfillment.LabelService" => "Fulfillment.LabelService",
            "Fulfillment.ConfigService" => "Fulfillment.ConfigService",
            "all" => "Fulfillment.LabelService|Fulfillment.ConfigService",
            _ => null
        };
        if (service is null) return "service_name must be Fulfillment.LabelService, Fulfillment.ConfigService, or all.";

        var logQuery = service.Contains("|", StringComparison.Ordinal)
            ? $"{{service_name=~\"{service}\"}}"
            : $"{{service_name=\"{service}\"}}";
        if (!string.IsNullOrWhiteSpace(contains))
            logQuery += " |= " + JsonSerializer.Serialize(contains.Trim()[..Math.Min(contains.Trim().Length, 100)]);
        var end = DateTimeOffset.UtcNow;
        var start = end.AddMinutes(-lookback_minutes);
        var queryString = $"query={Uri.EscapeDataString(logQuery)}&start={start.ToUnixTimeMilliseconds()}000000&end={end.ToUnixTimeMilliseconds()}000000&limit=50&direction=backward";

        try
        {
            var baseUrl = configuration["Loki:BaseUrl"] ?? "http://localhost:3100/";
            var client = CreateClient("Loki:BaseUrl", baseUrl);
            using var response = await client.GetAsync("loki/api/v1/query_range?" + queryString, cancellationToken);
            if (!response.IsSuccessStatusCode) return await ReadResponseAsync(response, cancellationToken);
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var entries = new List<object>();
            foreach (var stream in document.RootElement.GetProperty("data").GetProperty("result").EnumerateArray())
            {
                var labels = stream.GetProperty("stream");
                var serviceLabel = labels.TryGetProperty("service_name", out var name) ? name.GetString() : service;
                foreach (var value in stream.GetProperty("values").EnumerateArray())
                {
                    var parts = value.EnumerateArray().ToArray();
                    entries.Add(new { service = serviceLabel, timestampUnixNanos = parts[0].GetString(), message = Redact(parts[1].GetString() ?? string.Empty) });
                    if (entries.Count >= 50) break;
                }
                if (entries.Count >= 50) break;
            }
            return JsonSerializer.Serialize(new { service = service_name.Trim().Equals("all", StringComparison.OrdinalIgnoreCase) ? "Fulfillment.LabelService + Fulfillment.ConfigService" : service, lookbackMinutes = lookback_minutes, count = entries.Count, entries }, JsonOptions);
        }
        catch (Exception exception) when (IsDependencyException(exception))
        {
            return $"Loki log search unavailable: {SafeMessage(exception)}";
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string BuildQuery(IReadOnlyDictionary<string, string> parameters) =>
        string.Join("&", parameters.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

    private HttpClient CreateClient(string setting, string defaultBaseUrl)
    {
        var baseUrl = configuration[setting] ?? defaultBaseUrl;
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(5);
        return client;
    }

    private static async Task<string> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (content.Length > 30_000) content = content[..30_000] + "… [truncated]";
        return $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n{Redact(content)}";
    }

    private static string Redact(string value)
    {
        var redacted = SecretPattern.Replace(value, "$1[REDACTED]");
        redacted = BearerPattern.Replace(redacted, "Bearer [REDACTED]");
        redacted = EmailPattern.Replace(redacted, "[REDACTED_EMAIL]");
        return redacted.Length > 2_000 ? redacted[..2_000] + "… [truncated]" : redacted;
    }

    private static bool IsDependencyException(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or TimeoutException or NpgsqlException or RedisException or SocketException or JsonException;

    private static string SafeMessage(Exception exception) =>
        exception is TaskCanceledException ? "request timed out" : exception.GetType().Name;
}

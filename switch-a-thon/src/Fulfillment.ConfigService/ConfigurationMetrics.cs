using System.Diagnostics.Metrics;

namespace Fulfillment.ConfigService;

public static class ConfigurationMetrics
{
    public const string MeterName = "ResolveIQ.ConfigService";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> LookupFailures = Meter.CreateCounter<long>(
        "resolveiq_configuration_lookup_failures", unit: "failures", description: "Unmatched zone and rate lookups.");

    public static void RecordFailure(string lookupType, string errorCode) =>
        LookupFailures.Add(1,
            new KeyValuePair<string, object?>("lookup_type", lookupType),
            new KeyValuePair<string, object?>("error_code", errorCode));
}

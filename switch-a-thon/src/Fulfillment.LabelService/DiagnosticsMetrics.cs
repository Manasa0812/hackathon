using System.Diagnostics.Metrics;

namespace Fulfillment.LabelService;

public static class DiagnosticsMetrics
{
    public const string MeterName = "ResolveIQ.LabelService";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> LabelAttempts = Meter.CreateCounter<long>(
        "resolveiq_label_attempts", unit: "attempts", description: "Label generation attempts by outcome and error code.");

    public static void RecordLabelAttempt(string outcome, string? errorCode, int httpStatusCode) =>
        LabelAttempts.Add(1,
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("error_code", errorCode ?? "none"),
            new KeyValuePair<string, object?>("http_status_code", httpStatusCode));
}

using System.Diagnostics.Metrics;

namespace LN_history.Api.Instrumentation;

public class AppMetrics
{
    public const string MeterName = "LN_history.Api";

    public AppMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        // Histogram: Best for measuring duration (latency)
        // Unit: Seconds (Prometheus standard)
        DbQueryDuration = meter.CreateHistogram<double>(
            "ln_db_query_duration_seconds",
            unit: "s",
            description: "Time taken to execute the SQL query");

        StreamingDuration = meter.CreateHistogram<double>(
            "ln_streaming_duration_seconds",
            unit: "s",
            description: "Time taken to stream the result to the client");
            
        SnapshotGenerationDuration = meter.CreateHistogram<double>(
            "ln_snapshot_generation_duration_seconds",
            unit: "s",
            description: "Total time for snapshot generation request");
    }

    public Histogram<double> DbQueryDuration { get; }
    public Histogram<double> StreamingDuration { get; }
    public Histogram<double> SnapshotGenerationDuration { get; }
}
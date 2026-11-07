namespace LN_History.Model;

/// <summary>
/// One authenticated request, appended to <c>api.request_log</c> by the async flush. Unlike
/// <see cref="UsageDelta"/> (a per-day aggregate), this keeps the exact path and query so an
/// operator can see what a key actually asked for. <see cref="KeyId"/> is null for the emergency
/// admin, which has no <c>api_keys</c> row.
/// </summary>
public sealed class RequestLogEntry
{
    public DateTime Timestamp { get; init; }
    public long? KeyId { get; init; }
    public string Method { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    /// <summary>Query string with the <c>api_key</c> parameter removed; null when empty.</summary>
    public string? Query { get; init; }
    /// <summary>The cost route key, or null for routes that are not charged.</summary>
    public string? RouteKey { get; init; }
    public int StatusCode { get; init; }
    /// <summary>Budget units actually charged (0 when refunded or rejected).</summary>
    public int Cost { get; init; }
    /// <summary>allowed | refunded | burst_exceeded | budget_exceeded | concurrency_exceeded, or null when not charged.</summary>
    public string? ChargeOutcome { get; init; }
    public long DurationMs { get; init; }
    public long Bytes { get; init; }
    public string? ClientIp { get; init; }
    public string? UserAgent { get; init; }
    public string? TraceId { get; init; }
}

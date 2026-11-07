namespace LN_history.Core.Auth;

/// <summary>
/// Global defaults for the API-key rate limiter (bound from the <c>ApiAccess</c> config section) plus
/// the emergency admin key. Per-key columns in <c>api.api_keys</c> override the <c>Default*</c> values.
/// </summary>
public sealed class ApiAccessOptions
{
    /// <summary>Cost units a researcher key may spend per UTC day when the key sets no override.</summary>
    public int DefaultDailyBudget { get; set; } = 1000;

    /// <summary>Sustained request rate (tokens/sec) for the per-key burst bucket.</summary>
    public int DefaultBurstPerSec { get; set; } = 5;

    /// <summary>Burst bucket capacity (max requests absorbed instantaneously).</summary>
    public int DefaultBurstBucket { get; set; } = 10;

    /// <summary>Concurrent WebSocket stream connections a researcher key may hold.</summary>
    public int DefaultMaxStreamConns { get; set; } = 2;

    /// <summary>Concurrent heavy requests (snapshot, diff, stream backfill) a researcher key may run.</summary>
    public int MaxConcurrentHeavyPerKey { get; set; } = 1;

    /// <summary>How often the in-memory key + weight caches are reloaded from the DB.</summary>
    public int ReloadSeconds { get; set; } = 30;

    /// <summary>How often accumulated usage deltas are flushed to <c>api.usage</c>.</summary>
    public int FlushSeconds { get; set; } = 5;

    /// <summary>
    /// The static config <c>ApiKey</c>, honoured as an always-valid unlimited admin so an empty or
    /// unreachable <c>api</c> schema can never lock the operator out. Null/empty disables the emergency key.
    /// </summary>
    public string? EmergencyAdminKey { get; set; }
}

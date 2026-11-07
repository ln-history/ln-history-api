using LN_History.Model;

namespace LN_history.Data.DataStores;

/// <summary>
/// Durable access to the <c>api</c> schema (keys, endpoint weights, usage ledger, request log). Used only by the
/// maintenance background service — at startup and on the periodic reload/flush cycle — never on the
/// request hot path (enforcement is in-memory).
/// </summary>
public interface IApiKeyStore
{
    /// <summary>All enabled keys, for the in-memory auth cache.</summary>
    Task<IReadOnlyList<ApiKeyRecord>> LoadKeysAsync(CancellationToken cancellationToken);

    /// <summary>route_key -&gt; cost weight, for the in-memory weight cache.</summary>
    Task<IReadOnlyDictionary<string, int>> LoadWeightsAsync(CancellationToken cancellationToken);

    /// <summary>key_id -&gt; total cost already spent on the given UTC day (to seed budget counters on startup).</summary>
    Task<IReadOnlyDictionary<long, long>> LoadTodaySpendAsync(DateOnly utcDay, CancellationToken cancellationToken);

    /// <summary>Additively upsert usage deltas into <c>api.usage</c> and stamp <c>last_used_at</c> for active keys.</summary>
    Task FlushUsageAsync(IReadOnlyCollection<UsageDelta> deltas, CancellationToken cancellationToken);

    /// <summary>Append per-request rows to <c>api.request_log</c> (binary COPY).</summary>
    Task InsertRequestLogAsync(IReadOnlyCollection<RequestLogEntry> entries, CancellationToken cancellationToken);
}

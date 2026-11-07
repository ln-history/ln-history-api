using LN_History.Model;

namespace LN_history.Core.Auth;

/// <summary>
/// In-memory authority for API-key auth + cost-based rate limiting. All request-path methods are
/// lock-cheap and never touch the DB; the maintenance service feeds it (ApplyKeysAndWeights /
/// SeedTodaySpend) and drains it (DrainPendingUsage) for the durable ledger. Singleton.
/// </summary>
public interface IApiAccessService
{
    /// <summary>Resolve a presented raw key to its principal, or null if unknown/disabled/expired.</summary>
    ApiKeyRecord? Authenticate(string presentedKey);

    /// <summary>Cost weight for a route key, or <paramref name="fallback"/> if not configured.</summary>
    int ResolveWeight(string routeKey, int fallback);

    /// <summary>Pre-charge burst + budget. Admins are always allowed and never charged.</summary>
    ChargeResult Charge(ApiKeyRecord key, int cost);

    /// <summary>Return budget spent by a charge whose request then failed (admins ignored).</summary>
    void Refund(ApiKeyRecord key, int cost);

    /// <summary>Record measured actuals for the durable usage ledger (skipped for the emergency admin, id 0).</summary>
    void RecordActuals(ApiKeyRecord key, string endpoint, int cost, long durationMs, long bytes);

    /// <summary>Reserve one concurrent stream slot for the key; false if at its cap. Admins unlimited.</summary>
    bool TryAcquireStream(ApiKeyRecord key);

    /// <summary>Release a stream slot reserved by <see cref="TryAcquireStream"/>.</summary>
    void ReleaseStream(ApiKeyRecord key);

    /// <summary>Reserve one heavy-request slot for the key; false if at its cap. Admins unlimited.</summary>
    bool TryAcquireHeavy(ApiKeyRecord key);

    /// <summary>Release a heavy-request slot reserved by <see cref="TryAcquireHeavy"/>.</summary>
    void ReleaseHeavy(ApiKeyRecord key);

    // --- maintenance surface (called by ApiAccessMaintenanceService) ---
    void ApplyKeysAndWeights(IReadOnlyList<ApiKeyRecord> keys, IReadOnlyDictionary<string, int> weights);
    void SeedTodaySpend(IReadOnlyDictionary<long, long> spend, DateOnly utcDay);
    IReadOnlyCollection<UsageDelta> DrainPendingUsage();
}

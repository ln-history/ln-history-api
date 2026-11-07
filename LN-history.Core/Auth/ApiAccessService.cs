using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LN_History.Model;

namespace LN_history.Core.Auth;

/// <summary>
/// In-memory implementation of <see cref="IApiAccessService"/>. Keys and weights are swapped in
/// atomically by the maintenance service; per-key counters (daily budget, burst token bucket, stream
/// slots) live here and are authoritative for enforcement. Usage actuals accumulate into a pending
/// map that the maintenance service drains and flushes to <c>api.usage</c>.
/// </summary>
public sealed class ApiAccessService : IApiAccessService
{
    private readonly ApiAccessOptions _options;
    private readonly string? _emergencyHash;

    private volatile IReadOnlyDictionary<string, ApiKeyRecord> _keysByHash =
        new Dictionary<string, ApiKeyRecord>();
    private volatile IReadOnlyDictionary<string, int> _weights =
        new Dictionary<string, int>();

    private readonly ConcurrentDictionary<long, KeyCounters> _counters = new();
    private ConcurrentDictionary<UsageKey, UsageAccum> _pending = new();

    public ApiAccessService(ApiAccessOptions options)
    {
        _options = options;
        _emergencyHash = string.IsNullOrWhiteSpace(options.EmergencyAdminKey)
            ? null
            : Sha256Hex(options.EmergencyAdminKey);
    }

    private static readonly ApiKeyRecord EmergencyAdmin = new()
    {
        Id = 0,
        Role = KeyRole.Admin,
        OwnerLabel = "emergency-admin",
        DisplayPrefix = "emergency",
        Enabled = true,
    };

    public ApiKeyRecord? Authenticate(string presentedKey)
    {
        if (string.IsNullOrEmpty(presentedKey)) return null;
        var hash = Sha256Hex(presentedKey);

        if (_emergencyHash is not null &&
            CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(_emergencyHash)))
            return EmergencyAdmin;

        if (_keysByHash.TryGetValue(hash, out var key))
        {
            if (key.ExpiresAt is { } expiry && expiry <= DateTime.UtcNow) return null;
            return key;
        }
        return null;
    }

    public int ResolveWeight(string routeKey, int fallback) =>
        _weights.TryGetValue(routeKey, out var w) ? w : fallback;

    public ChargeResult Charge(ApiKeyRecord key, int cost)
    {
        if (key.IsAdmin) return ChargeResult.AdminAllowed;

        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var budget = key.DailyBudget ?? _options.DefaultDailyBudget;
        var burstRate = key.BurstPerSec ?? _options.DefaultBurstPerSec;
        var bucket = Math.Max(_options.DefaultBurstBucket, 1);

        var counters = _counters.GetOrAdd(key.Id, _ => new KeyCounters(today, 0, bucket, now));
        var (outcome, remaining) = counters.TryCharge(cost, budget, burstRate, bucket, today, now);

        var resetUnix = NextUtcMidnightUnix(now);
        var retryAfter = outcome switch
        {
            ChargeOutcome.BurstExceeded => 1,
            ChargeOutcome.BudgetExceeded => (int)Math.Max(1, resetUnix - ToUnix(now)),
            _ => 0,
        };
        return new ChargeResult(outcome, remaining, resetUnix, retryAfter);
    }

    public void Refund(ApiKeyRecord key, int cost)
    {
        if (key.IsAdmin || cost <= 0) return;
        if (_counters.TryGetValue(key.Id, out var counters))
            counters.RefundBudget(cost, DateOnly.FromDateTime(DateTime.UtcNow));
    }

    public void RecordActuals(ApiKeyRecord key, string endpoint, int cost, long durationMs, long bytes)
    {
        if (key.Id <= 0) return; // emergency admin has no api_keys row -> would violate the usage FK
        var day = DateOnly.FromDateTime(DateTime.UtcNow);
        var acc = _pending.GetOrAdd(new UsageKey(key.Id, day, endpoint), _ => new UsageAccum());
        Interlocked.Add(ref acc.Cost, cost);
        Interlocked.Increment(ref acc.Count);
        Interlocked.Add(ref acc.DurationMs, durationMs);
        Interlocked.Add(ref acc.Bytes, bytes);
    }

    public bool TryAcquireStream(ApiKeyRecord key)
    {
        if (key.IsAdmin) return true;
        var max = key.MaxStreamConns ?? _options.DefaultMaxStreamConns;
        var counters = _counters.GetOrAdd(key.Id,
            _ => new KeyCounters(DateOnly.FromDateTime(DateTime.UtcNow), 0, _options.DefaultBurstBucket, DateTime.UtcNow));
        return counters.TryAcquireStream(max);
    }

    public void ReleaseStream(ApiKeyRecord key)
    {
        if (key.IsAdmin) return;
        if (_counters.TryGetValue(key.Id, out var counters))
            counters.ReleaseStream();
    }

    public bool TryAcquireHeavy(ApiKeyRecord key)
    {
        if (key.IsAdmin) return true;
        var counters = _counters.GetOrAdd(key.Id,
            _ => new KeyCounters(DateOnly.FromDateTime(DateTime.UtcNow), 0, _options.DefaultBurstBucket, DateTime.UtcNow));
        return counters.TryAcquireHeavy(Math.Max(_options.MaxConcurrentHeavyPerKey, 1));
    }

    public void ReleaseHeavy(ApiKeyRecord key)
    {
        if (key.IsAdmin) return;
        if (_counters.TryGetValue(key.Id, out var counters))
            counters.ReleaseHeavy();
    }

    public void ApplyKeysAndWeights(IReadOnlyList<ApiKeyRecord> keys, IReadOnlyDictionary<string, int> weights)
    {
        _keysByHash = keys.ToDictionary(k => k.KeyHash, k => k);
        _weights = weights;

        // Drop counters for keys that no longer exist (revoked) so they don't leak memory.
        var live = new HashSet<long>(keys.Select(k => k.Id));
        foreach (var id in _counters.Keys)
            if (!live.Contains(id))
                _counters.TryRemove(id, out _);
    }

    public void SeedTodaySpend(IReadOnlyDictionary<long, long> spend, DateOnly utcDay)
    {
        foreach (var (keyId, spent) in spend)
        {
            var counters = _counters.GetOrAdd(keyId,
                _ => new KeyCounters(utcDay, 0, _options.DefaultBurstBucket, DateTime.UtcNow));
            counters.SeedSpend(utcDay, spent);
        }
    }

    public IReadOnlyCollection<UsageDelta> DrainPendingUsage()
    {
        var drained = Interlocked.Exchange(ref _pending, new ConcurrentDictionary<UsageKey, UsageAccum>());
        if (drained.IsEmpty) return Array.Empty<UsageDelta>();
        return drained.Select(kv => new UsageDelta
        {
            KeyId = kv.Key.KeyId,
            UsageDay = kv.Key.Day,
            Endpoint = kv.Key.Endpoint,
            Cost = kv.Value.Cost,
            Count = kv.Value.Count,
            DurationMs = kv.Value.DurationMs,
            Bytes = kv.Value.Bytes,
        }).ToList();
    }

    // --- helpers ---

    internal static string Sha256Hex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes);
    }

    private static long ToUnix(DateTime utc) => new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();

    private static long NextUtcMidnightUnix(DateTime now)
    {
        var nextMidnight = now.Date.AddDays(1); // Date is midnight; now is UTC
        return new DateTimeOffset(nextMidnight, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    private readonly record struct UsageKey(long KeyId, DateOnly Day, string Endpoint);

    private sealed class UsageAccum
    {
        public long Cost;
        public long Count;
        public long DurationMs;
        public long Bytes;
    }

    /// <summary>Per-key daily budget + burst token bucket + stream-slot count, guarded by one lock.</summary>
    private sealed class KeyCounters
    {
        private readonly object _lock = new();
        private DateOnly _day;
        private long _spentToday;
        private double _tokens;
        private DateTime _lastRefill;
        private int _streamConns;
        private int _heavyInFlight;

        public KeyCounters(DateOnly day, long spentToday, double initialTokens, DateTime now)
        {
            _day = day;
            _spentToday = spentToday;
            _tokens = initialTokens;
            _lastRefill = now;
        }

        public (ChargeOutcome Outcome, long Remaining) TryCharge(
            int cost, int budget, double burstRate, double bucket, DateOnly today, DateTime now)
        {
            lock (_lock)
            {
                if (_day != today) { _day = today; _spentToday = 0; }

                var elapsed = (now - _lastRefill).TotalSeconds;
                if (elapsed > 0)
                {
                    _tokens = Math.Min(bucket, _tokens + elapsed * burstRate);
                    _lastRefill = now;
                }

                if (_tokens < 1.0)
                    return (ChargeOutcome.BurstExceeded, Math.Max(0, budget - _spentToday));
                if (_spentToday + cost > budget)
                    return (ChargeOutcome.BudgetExceeded, Math.Max(0, budget - _spentToday));

                _tokens -= 1.0;
                _spentToday += cost;
                return (ChargeOutcome.Allowed, Math.Max(0, budget - _spentToday));
            }
        }

        public void RefundBudget(int cost, DateOnly today)
        {
            lock (_lock)
                if (_day == today)
                    _spentToday = Math.Max(0, _spentToday - cost);
        }

        public void SeedSpend(DateOnly day, long spent)
        {
            lock (_lock) { _day = day; _spentToday = spent; }
        }

        public bool TryAcquireStream(int max)
        {
            lock (_lock)
            {
                if (_streamConns >= max) return false;
                _streamConns++;
                return true;
            }
        }

        public void ReleaseStream()
        {
            lock (_lock)
                if (_streamConns > 0)
                    _streamConns--;
        }

        public bool TryAcquireHeavy(int max)
        {
            lock (_lock)
            {
                if (_heavyInFlight >= max) return false;
                _heavyInFlight++;
                return true;
            }
        }

        public void ReleaseHeavy()
        {
            lock (_lock)
                if (_heavyInFlight > 0)
                    _heavyInFlight--;
        }
    }
}

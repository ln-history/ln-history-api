using System.Globalization;

namespace LN_history.Api.Infrastructure;

/// <summary>Interpretation of a timestamp query value: absent (all), the literal "now", or an instant.</summary>
public readonly record struct TimeQuery(bool IsAll, bool IsNow, DateTime? At)
{
    /// <summary>The instant to use where "all" is not meaningful (now resolves to UtcNow).</summary>
    public DateTime? AsOf => IsNow ? DateTime.UtcNow : At;
}

public static class QueryHelpers
{
    public const int DefaultLimit = 1000;
    public const int MaxLimit = 10000;

    private const DateTimeStyles InstantStyles = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

    /// <summary>Parses a timestamp query value: null/empty =&gt; all, "now" =&gt; now, otherwise an ISO instant.</summary>
    public static bool TryParseTime(string? raw, out TimeQuery query)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            query = new TimeQuery(IsAll: true, IsNow: false, At: null);
            return true;
        }

        if (raw.Equals("now", StringComparison.OrdinalIgnoreCase))
        {
            query = new TimeQuery(IsAll: false, IsNow: true, At: null);
            return true;
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, InstantStyles, out var instant))
        {
            query = new TimeQuery(IsAll: false, IsNow: false, At: instant);
            return true;
        }

        query = default;
        return false;
    }

    /// <summary>Parses a required instant (path segment): "now" or an ISO instant; "all" is not allowed.</summary>
    public static bool TryParseInstant(string? raw, out DateTime instant)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (raw.Equals("now", StringComparison.OrdinalIgnoreCase))
            {
                instant = DateTime.UtcNow;
                return true;
            }

            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, InstantStyles, out instant))
            {
                return true;
            }
        }

        instant = default;
        return false;
    }

    public static (int Limit, int Offset) ClampPage(int? limit, int? offset)
    {
        var clampedLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var clampedOffset = Math.Max(offset ?? 0, 0);
        return (clampedLimit, clampedOffset);
    }
}

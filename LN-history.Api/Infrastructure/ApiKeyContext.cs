using LN_History.Model;

namespace LN_history.Api.Infrastructure;

/// <summary>
/// Carries the authenticated key principal through the request via <see cref="HttpContext.Items"/>.
/// Null means auth is disabled (Development) — callers treat that as unlimited/no-limit.
/// Also carries what the request was charged, so the auth middleware can write it to the request log.
/// </summary>
public static class ApiKeyContext
{
    public const string ItemKey = "__ln_api_key";
    private const string ChargeItemKey = "__ln_api_charge";
    private const string StreamBytesItemKey = "__ln_api_stream_bytes";

    public static void Set(HttpContext context, ApiKeyRecord record) => context.Items[ItemKey] = record;

    public static ApiKeyRecord? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as ApiKeyRecord : null;

    /// <summary>What the request was charged: route key, units kept after any refund, and the outcome tag.</summary>
    public sealed record ChargeInfo(string RouteKey, int Cost, string Outcome);

    public static void SetCharge(HttpContext context, string routeKey, int cost, string outcome) =>
        context.Items[ChargeItemKey] = new ChargeInfo(routeKey, cost, outcome);

    public static ChargeInfo? GetCharge(HttpContext context) =>
        context.Items.TryGetValue(ChargeItemKey, out var value) ? value as ChargeInfo : null;

    /// <summary>Bytes sent over a WebSocket stream, which never pass through the response body.</summary>
    public static void AddStreamBytes(HttpContext context, long bytes) =>
        context.Items[StreamBytesItemKey] = GetStreamBytes(context) + bytes;

    public static long GetStreamBytes(HttpContext context) =>
        context.Items.TryGetValue(StreamBytesItemKey, out var value) && value is long b ? b : 0;
}

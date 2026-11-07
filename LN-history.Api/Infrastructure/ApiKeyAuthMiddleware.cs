using System.Diagnostics;
using System.Text.Json;
using LN_history.Api.Instrumentation;
using LN_history.Core.Auth;
using LN_History.Model;
using Microsoft.Extensions.Logging;

namespace LN_history.Api.Infrastructure;

/// <summary>
/// Authenticates each request against the in-memory key cache and attaches the principal to
/// <see cref="HttpContext.Items"/> for the cost filter. Swagger is always reachable. Browsers cannot
/// set headers on a WebSocket handshake, so for upgrade requests the key may be supplied as the
/// <c>api_key</c> query parameter instead of <c>x-api-key</c>. Only added to the pipeline when
/// <c>ApiKeyMiddleware:Enabled</c> is true (off in Development ⇒ no auth, no limits).
/// Every authenticated request — including ones refused with 429 — is buffered for
/// <c>api.request_log</c> with its exact path, query, status, charge, duration, bytes and client.
/// </summary>
public sealed class ApiKeyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;

    public ApiKeyAuthMiddleware(RequestDelegate next, ILogger<ApiKeyAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IApiAccessService access, AppMetrics metrics)
    {
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        var presented = ExtractKey(context.Request);
        if (string.IsNullOrEmpty(presented))
        {
            metrics.AuthResults.Add(1, new KeyValuePair<string, object?>("result", "missing_key"));
            _logger.LogInformation("Rejected {Method} {Path}: no API key presented.",
                context.Request.Method, context.Request.Path);
            await WriteProblemAsync(context, "API key is missing.");
            return;
        }

        var record = access.Authenticate(presented);
        if (record is null)
        {
            metrics.AuthResults.Add(1, new KeyValuePair<string, object?>("result", "invalid_key"));
            // The key itself is never logged — only the fact that one was presented and rejected.
            _logger.LogWarning("Rejected {Method} {Path}: unknown, disabled or expired API key.",
                context.Request.Method, context.Request.Path);
            await WriteProblemAsync(context, "Invalid or expired API key.");
            return;
        }

        metrics.AuthResults.Add(1, new KeyValuePair<string, object?>("result", "ok"));

        // Tag the server span so a trace in Tempo can be attributed to a tenant. Cardinality is the
        // number of issued keys (a handful), and these are span attributes only — Tempo's
        // span-metrics dimensions are configured separately, so no metric series are added.
        var activity = Activity.Current;
        if (activity is not null)
        {
            activity.SetTag("lnhistory.key.owner", record.OwnerLabel);
            activity.SetTag("lnhistory.key.role", record.IsAdmin ? "admin" : "researcher");
        }

        ApiKeyContext.Set(context, record);

        var startedAt = DateTime.UtcNow;
        var sw = Stopwatch.StartNew();
        var isWebSocket = context.WebSockets.IsWebSocketRequest;
        var originalBody = context.Response.Body;
        // WebSocket frames bypass the response body; the stream controller counts those itself.
        var counter = isWebSocket ? null : new CountingStream(originalBody);
        if (counter is not null) context.Response.Body = counter;

        var threw = false;
        try
        {
            await _next(context);
        }
        catch
        {
            threw = true;
            throw;
        }
        finally
        {
            if (counter is not null) context.Response.Body = originalBody;
            sw.Stop();
            var bytes = counter?.BytesWritten ?? ApiKeyContext.GetStreamBytes(context);
            access.RecordRequest(BuildEntry(context, record, startedAt, sw.ElapsedMilliseconds, bytes, threw));
        }
    }

    /// <summary>Assemble the request-log row. The <c>api_key</c> query parameter is never stored.</summary>
    public static RequestLogEntry BuildEntry(
        HttpContext context, ApiKeyRecord record, DateTime startedAt, long durationMs, long bytes, bool threw)
    {
        var request = context.Request;
        var charge = ApiKeyContext.GetCharge(context);
        return new RequestLogEntry
        {
            Timestamp = startedAt,
            KeyId = record.Id > 0 ? record.Id : null, // emergency admin has no api_keys row
            Method = request.Method,
            Path = request.Path.Value ?? string.Empty,
            Query = StripApiKey(request.Query),
            RouteKey = charge?.RouteKey,
            // An exception escaping the pipeline becomes a 500 even if no status was written yet.
            StatusCode = threw ? StatusCodes.Status500InternalServerError : context.Response.StatusCode,
            Cost = charge?.Cost ?? 0,
            ChargeOutcome = charge?.Outcome,
            DurationMs = durationMs,
            Bytes = bytes,
            ClientIp = ClientIp(context),
            UserAgent = Truncate(request.Headers.UserAgent.ToString(), 512),
            TraceId = Activity.Current?.TraceId.ToString(),
        };
    }

    public static string? StripApiKey(IQueryCollection query)
    {
        var kept = query
            .Where(kv => !kv.Key.Equals("api_key", StringComparison.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value.Select(v => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(v ?? string.Empty)}"))
            .ToList();
        return kept.Count == 0 ? null : Truncate(string.Join('&', kept), 2048);
    }

    /// <summary>
    /// Requests reach us through Cloudflare and nginx, so the connection peer is a Cloudflare edge.
    /// Cloudflare puts the real client in CF-Connecting-IP; fall back to the peer if it is missing.
    /// </summary>
    private static string? ClientIp(HttpContext context)
    {
        var cf = context.Request.Headers["CF-Connecting-IP"].ToString();
        if (!string.IsNullOrWhiteSpace(cf)) return cf.Trim();
        return context.Connection.RemoteIpAddress?.ToString();
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? null : value.Length <= max ? value : value[..max];

    /// <summary>Pass-through response body that counts what is written, for the request log.</summary>
    private sealed class CountingStream : Stream
    {
        private readonly Stream _inner;
        public long BytesWritten { get; private set; }

        public CountingStream(Stream inner) => _inner = inner;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            BytesWritten += count;
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken);
            BytesWritten += count;
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _inner.WriteAsync(buffer, cancellationToken);
            BytesWritten += buffer.Length;
        }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static string? ExtractKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue("x-api-key", out var header))
            return header.ToString();

        var isWebSocket = request.Headers.TryGetValue("Upgrade", out var upgrade)
                          && upgrade.ToString().Equals("websocket", StringComparison.OrdinalIgnoreCase);
        if (isWebSocket && request.Query.TryGetValue("api_key", out var queryKey))
            return queryKey.ToString();

        return null;
    }

    private static async Task WriteProblemAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/problem+json";
        var problem = new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.2",
            title = "Unauthorized",
            status = StatusCodes.Status401Unauthorized,
            detail
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}

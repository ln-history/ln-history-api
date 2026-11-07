using System.Diagnostics;
using System.Net.WebSockets;
using Asp.Versioning;
using LN_history.Api.Infrastructure;
using LN_history.Api.Instrumentation;
using LN_history.Core.Auth;
using LN_history.Core.Services;
using LN_History.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace LN_history.Api.Controllers;

/// <summary>
/// WebSocket endpoint streaming raw BOLT #7 gossip as it is ingested. Each message is a binary
/// frame carrying one raw gossip envelope (varint(len) ++ uint16(type) ++ payload) — the same
/// bytes the snapshot/diff endpoints return, parseable with lnhistoryclient.
/// </summary>
[ApiController]
[ApiVersion(1)]
[Route("ln-history/v{v:apiVersion}/stream")]
[ApiExplorerSettings(IgnoreApi = true)] // WebSocket upgrade — Swagger cannot meaningfully describe it
public class GossipStreamController : ControllerBase
{
    /// <summary>Hard cap on reconnect backfill so a client with a very old cursor can't ask for millions of rows.</summary>
    private const int MaxBackfill = 50_000;

    /// <summary>route_key for charging the reconnect backfill query against the daily budget.</summary>
    private const string BackfillRouteKey = "stream_backfill";
    private const int BackfillDefaultWeight = 40;

    private readonly IGossipStreamService _stream;
    private readonly IApiAccessService _access;
    private readonly AppMetrics _metrics;
    private readonly ILogger<GossipStreamController> _logger;

    public GossipStreamController(
        IGossipStreamService stream,
        IApiAccessService access,
        AppMetrics metrics,
        ILogger<GossipStreamController> logger)
    {
        _stream = stream;
        _access = access;
        _metrics = metrics;
        _logger = logger;
    }

    /// <summary>
    /// WebSocket upgrade. Query params: <c>types</c> = comma-separated BOLT #7 types to receive
    /// (e.g. <c>256,257,258</c>; default all); <c>since</c> = replay gossip with internal_id greater
    /// than this value before switching to live (reconnect catch-up). Because browsers cannot set
    /// headers on a WebSocket handshake, the API key may be supplied as the <c>api_key</c> query
    /// param instead of the <c>x-api-key</c> header. A non-WebSocket request receives 400.
    /// </summary>
    [HttpGet]
    public async Task Get(
        [FromQuery(Name = "types")] string? types = null,
        [FromQuery(Name = "since")] long? since = null,
        CancellationToken cancellationToken = default)
    {
        if (!HttpContext.WebSockets.IsWebSocketRequest)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsync("This endpoint requires a WebSocket upgrade.", cancellationToken);
            return;
        }

        // Enforce the per-key concurrent-connection cap BEFORE the upgrade (can't send HTTP status
        // after it). record is null only when auth is disabled (Development) => unlimited.
        var record = ApiKeyContext.Get(HttpContext);
        var slotHeld = false;
        var heavyHeld = false;
        if (record is not null)
        {
            if (!_access.TryAcquireStream(record))
            {
                _metrics.StreamRejections.Add(1);
                _logger.LogWarning("Refused stream upgrade for {Owner}: already at the concurrent-connection cap.",
                    record.OwnerLabel);
                HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await HttpContext.Response.WriteAsync("Too many concurrent stream connections for this key.", cancellationToken);
                return;
            }
            slotHeld = true;
        }

        try
        {
            var typeFilter = ParseTypes(types);
            using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            // Charge the reconnect backfill (the only DB-touching part of the stream) against the
            // budget. The live tail is free (in-memory fan-out). Admins bypass.
            if (since is { } cursor)
            {
                if (record is { IsAdmin: false })
                {
                    // The backfill query counts as a heavy request, same as snapshot/diff: one at a time per key.
                    if (!_access.TryAcquireHeavy(record))
                    {
                        ApiKeyContext.SetCharge(HttpContext, BackfillRouteKey, 0, "concurrency_exceeded");
                        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation,
                            "another heavy request is running", CancellationToken.None);
                        return;
                    }
                    heavyHeld = true;

                    var weight = _access.ResolveWeight(BackfillRouteKey, BackfillDefaultWeight);
                    var charge = _access.Charge(record, weight);
                    ApiKeyContext.SetCharge(HttpContext, BackfillRouteKey, charge.Allowed ? weight : 0,
                        charge.Outcome switch
                        {
                            ChargeOutcome.Allowed => "allowed",
                            ChargeOutcome.BurstExceeded => "burst_exceeded",
                            _ => "budget_exceeded",
                        });
                    if (!charge.Allowed)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation,
                            "backfill over budget", CancellationToken.None);
                        return;
                    }
                    _access.RecordActuals(record, BackfillRouteKey, weight, 0, 0);
                }
            }

            // Subscribe FIRST so live frames buffer while we run the backfill query; then dedup the
            // seam by internal_id. This yields no gap (live is buffered) and no duplicate (skip <= lastSent).
            using var subscription = _stream.Subscribe(typeFilter);
            var lastSent = long.MinValue;

            _metrics.StreamConnections.Add(1);
            var connectedFor = Stopwatch.StartNew();

            // A server that only ever sends never observes a client-initiated close handshake: the
            // close frame sits unread, the socket stays Open, and this connection would keep its
            // stream slot and subscriber buffer until the TCP connection itself died. Draining the
            // receive side turns a clean client close into prompt cancellation of the send loop.
            using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var drain = DrainClientAsync(socket, connectionCts);

            try
            {
                var token = connectionCts.Token;

                if (since is { } cursor2)
                {
                    IReadOnlyList<GossipFrame> backlog;
                    try
                    {
                        backlog = await _stream.GetBackfillAsync(cursor2, typeFilter, MaxBackfill, token);
                    }
                    finally
                    {
                        // Only the DB query is heavy; sending the frames is not.
                        if (heavyHeld) { _access.ReleaseHeavy(record!); heavyHeld = false; }
                    }
                    foreach (var frame in backlog)
                    {
                        await SendAsync(socket, frame.RawGossip, token);
                        ApiKeyContext.AddStreamBytes(HttpContext, frame.RawGossip.Length);
                        lastSent = frame.InternalId;
                    }
                }

                // The high-water mark is FROZEN at the end of the backfill. Advancing it with every
                // live frame instead would silently drop any frame that arrives out of internal_id
                // order — and NOTIFY order is commit order, which is not guaranteed to match the
                // order in which ids were assigned when writers commit concurrently. Only the
                // backfill seam needs dedup, so only the seam is filtered.
                var seam = lastSent;

                await foreach (var frame in subscription.ReadAllAsync(token))
                {
                    if (frame.InternalId <= seam) continue; // already delivered by the backfill above
                    await SendAsync(socket, frame.RawGossip, token);
                    ApiKeyContext.AddStreamBytes(HttpContext, frame.RawGossip.Length);
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected or the server is shutting down — normal.
            }
            catch (WebSocketException ex)
            {
                _logger.LogDebug(ex, "Gossip stream WebSocket closed by peer.");
            }
            finally
            {
                connectionCts.Cancel();
                await drain;   // never throws; must finish before CloseAsync touches the socket
                await CloseQuietlyAsync(socket);
                _metrics.StreamDuration.Record(connectedFor.Elapsed.TotalSeconds);
            }
        }
        finally
        {
            if (heavyHeld && record is not null)
                _access.ReleaseHeavy(record);
            if (slotHeld && record is not null)
                _access.ReleaseStream(record);
        }
    }

    /// <summary>
    /// Reads and discards anything the client sends, purely so a close frame is observed, and
    /// cancels <paramref name="connectionCts"/> when the client goes away so the send loop unblocks
    /// instead of sitting on <c>ReadAllAsync</c> forever. Never throws — it is awaited from a
    /// finally block.
    /// </summary>
    private static async Task DrainClientAsync(WebSocket socket, CancellationTokenSource connectionCts)
    {
        var scratch = new byte[128];
        try
        {
            while (!connectionCts.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(scratch, connectionCts.Token);
                if (result.MessageType == WebSocketMessageType.Close) return;
                // Clients are not expected to send data frames; ignore whatever arrives.
            }
        }
        catch (OperationCanceledException)
        {
            // Teardown initiated by the send side.
        }
        catch (WebSocketException)
        {
            // Peer vanished; the send loop will fail too.
        }
        catch (ObjectDisposedException)
        {
            // Socket already torn down.
        }
        finally
        {
            // Without this the send loop would keep waiting for the next gossip frame on a socket
            // whose client has already left, holding its stream slot and subscriber buffer.
            try { await connectionCts.CancelAsync(); } catch (ObjectDisposedException) { }
        }
    }

    private static ValueTask SendAsync(WebSocket socket, byte[] payload, CancellationToken cancellationToken) =>
        socket.SendAsync(payload.AsMemory(), WebSocketMessageType.Binary, endOfMessage: true, cancellationToken);

    private static async Task CloseQuietlyAsync(WebSocket socket)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // Peer already gone.
            }
        }
    }

    private static IReadOnlyCollection<int>? ParseTypes(string? types)
    {
        if (string.IsNullOrWhiteSpace(types)) return null;

        var parsed = types
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => int.TryParse(t, out var v) ? v : (int?)null)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .Distinct()
            .ToArray();

        return parsed.Length > 0 ? parsed : null;
    }
}

using Asp.Versioning;
using LN_history.Api.Infrastructure;
using LN_history.Api.Mapping;
using LN_history.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace LN_history.Api.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("ln-history/v{v:apiVersion}")]
public class SnapshotController : ControllerBase
{
    /// <summary>
    /// Widest window snapshot-diff will serve. The endpoint exists to catch up on recent gossip; a
    /// 1-day diff is already ~354K raw messages in one response, so longer ranges should be split
    /// into several calls or served by a snapshot.
    /// </summary>
    public static readonly TimeSpan MaxDiffWindow = TimeSpan.FromHours(2);

    private readonly ISnapshotService _snapshots;

    public SnapshotController(ISnapshotService snapshots)
    {
        _snapshots = snapshots;
    }

    /// <summary>All gossip valid at a timestamp as a raw byte stream. withUpdates adds active channel_updates.</summary>
    [HttpGet("snapshot/{timestamp}")]
    [ResourceCost("snapshot", 100, Heavy = true)]
    public async Task<IActionResult> GetSnapshot(
        string timestamp,
        [FromQuery(Name = "withUpdates")] bool withUpdates = false,
        CancellationToken cancellationToken = default)
    {
        if (!QueryHelpers.TryParseInstant(timestamp, out var instant))
            return Problem($"Invalid timestamp: '{timestamp}'.", statusCode: StatusCodes.Status400BadRequest);

        var bytes = await _snapshots.GetSnapshotAsync(instant, withUpdates, cancellationToken);
        return File(bytes, "application/octet-stream");
    }

    /// <summary>Gossip that appeared between two timestamps (at most <see cref="MaxDiffWindow"/> apart). rawGossip=true returns raw bytes; false returns ordered parsed events.</summary>
    [HttpGet("snapshot-diff/{start}/{end}")]
    [ResourceCost("snapshot_diff", 40, Heavy = true)]
    public async Task<IActionResult> GetSnapshotDiff(
        string start,
        string end,
        [FromQuery(Name = "rawGossip")] bool rawGossip = false,
        CancellationToken cancellationToken = default)
    {
        if (!QueryHelpers.TryParseInstant(start, out var startInstant))
            return Problem($"Invalid start timestamp: '{start}'.", statusCode: StatusCodes.Status400BadRequest);
        if (!QueryHelpers.TryParseInstant(end, out var endInstant))
            return Problem($"Invalid end timestamp: '{end}'.", statusCode: StatusCodes.Status400BadRequest);
        if (endInstant < startInstant)
            return Problem("end must be at or after start.", statusCode: StatusCodes.Status400BadRequest);
        if (endInstant - startInstant > MaxDiffWindow)
            return Problem($"The window between start and end may be at most {MaxDiffWindow.TotalMinutes:0} minutes.",
                statusCode: StatusCodes.Status400BadRequest);

        if (rawGossip)
        {
            var bytes = await _snapshots.GetDiffRawAsync(startInstant, endInstant, cancellationToken);
            return File(bytes, "application/octet-stream");
        }

        var events = await _snapshots.GetDiffEventsAsync(startInstant, endInstant, cancellationToken);
        return Ok(events.Select(e => e.ToDto()));
    }
}

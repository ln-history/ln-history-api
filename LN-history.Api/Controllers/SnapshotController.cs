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
    private readonly ISnapshotService _snapshots;

    public SnapshotController(ISnapshotService snapshots)
    {
        _snapshots = snapshots;
    }

    /// <summary>All gossip valid at a timestamp as a raw byte stream. withUpdates adds active channel_updates.</summary>
    [HttpGet("snapshot/{timestamp}")]
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

    /// <summary>Gossip that appeared between two timestamps. rawGossip=true returns raw bytes; false returns ordered parsed events.</summary>
    [HttpGet("snapshot-diff/{start}/{end}")]
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

        if (rawGossip)
        {
            var bytes = await _snapshots.GetDiffRawAsync(startInstant, endInstant, cancellationToken);
            return File(bytes, "application/octet-stream");
        }

        var events = await _snapshots.GetDiffEventsAsync(startInstant, endInstant, cancellationToken);
        return Ok(events.Select(e => e.ToDto()));
    }
}

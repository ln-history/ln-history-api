using Asp.Versioning;
using LN_history.Api.Infrastructure;
using LN_history.Api.Mapping;
using LN_history.Core.Services;
using LN_History.Model.Enums;
using Microsoft.AspNetCore.Mvc;

namespace LN_history.Api.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("ln-history/v{v:apiVersion}/stats")]
public class StatsController : ControllerBase
{
    private const int DefaultTopN = 10;
    private const int MaxTopN = 1000;

    private readonly IStatsService _stats;

    public StatsController(IStatsService stats)
    {
        _stats = stats;
    }

    /// <summary>Top channels by capacity | update_count | lifetime.</summary>
    [HttpGet("channels/top")]
    public async Task<IActionResult> TopChannels(
        [FromQuery(Name = "by")] string by = "capacity",
        [FromQuery(Name = "limit")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var rankBy = by.ToLowerInvariant() switch
        {
            "capacity" => ChannelRankBy.Capacity,
            "update_count" => ChannelRankBy.UpdateCount,
            "lifetime" => ChannelRankBy.Lifetime,
            _ => (ChannelRankBy?)null
        };
        if (rankBy is null)
            return Problem($"Invalid 'by': '{by}'. Expected capacity | update_count | lifetime.", statusCode: StatusCodes.Status400BadRequest);

        var results = await _stats.TopChannelsAsync(rankBy.Value, ClampTopN(limit), cancellationToken);
        return Ok(results.Select(r => r.ToDto()));
    }

    /// <summary>Top nodes by channels | announcements | capacity.</summary>
    [HttpGet("nodes/top")]
    public async Task<IActionResult> TopNodes(
        [FromQuery(Name = "by")] string by = "channels",
        [FromQuery(Name = "limit")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var rankBy = by.ToLowerInvariant() switch
        {
            "channels" => NodeRankBy.Channels,
            "announcements" => NodeRankBy.Announcements,
            "capacity" => NodeRankBy.Capacity,
            _ => (NodeRankBy?)null
        };
        if (rankBy is null)
            return Problem($"Invalid 'by': '{by}'. Expected channels | announcements | capacity.", statusCode: StatusCodes.Status400BadRequest);

        var results = await _stats.TopNodesAsync(rankBy.Value, ClampTopN(limit), cancellationToken);
        return Ok(results.Select(r => r.ToDto()));
    }

    /// <summary>Network-wide counts, currently or at a timestamp.</summary>
    [HttpGet("network")]
    public async Task<IActionResult> Network(
        [FromQuery(Name = "timestamp")] string? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        if (!QueryHelpers.TryParseTime(timestamp, out var time))
            return Problem($"Invalid timestamp: '{timestamp}'.", statusCode: StatusCodes.Status400BadRequest);

        var stats = await _stats.GetNetworkStatsAsync(time.AsOf, cancellationToken);
        return Ok(stats.ToDto());
    }

    /// <summary>Closure counts and mining-fee totals by type over an optional [from, to] window.</summary>
    [HttpGet("closures")]
    public async Task<IActionResult> Closures(
        [FromQuery(Name = "from")] string? from = null,
        [FromQuery(Name = "to")] string? to = null,
        CancellationToken cancellationToken = default)
    {
        DateTime? fromInstant = null;
        DateTime? toInstant = null;
        if (from is not null)
        {
            if (!QueryHelpers.TryParseInstant(from, out var f))
                return Problem($"Invalid from: '{from}'.", statusCode: StatusCodes.Status400BadRequest);
            fromInstant = f;
        }
        if (to is not null)
        {
            if (!QueryHelpers.TryParseInstant(to, out var t))
                return Problem($"Invalid to: '{to}'.", statusCode: StatusCodes.Status400BadRequest);
            toInstant = t;
        }

        var stats = await _stats.GetClosureStatsAsync(fromInstant, toInstant, cancellationToken);
        return Ok(stats.ToDto());
    }

    private static int ClampTopN(int? limit) => Math.Clamp(limit ?? DefaultTopN, 1, MaxTopN);
}

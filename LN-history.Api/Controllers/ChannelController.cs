using Asp.Versioning;
using LN_history.Api.Dto;
using LN_history.Api.Infrastructure;
using LN_history.Api.Mapping;
using LN_history.Core.Services;
using LN_History.Model;
using Microsoft.AspNetCore.Mvc;

namespace LN_history.Api.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("ln-history/v{v:apiVersion}/channels")]
public class ChannelController : ControllerBase
{
    private readonly IChannelService _channels;

    public ChannelController(IChannelService channels)
    {
        _channels = channels;
    }

    /// <summary>Single channel by scid (accepts "865123x1x0" or the 64-bit integer form).</summary>
    [HttpGet("{scid}")]
    public async Task<IActionResult> GetChannel(
        string scid,
        [FromQuery(Name = "nodeInformation")] bool nodeInformation = false,
        [FromQuery(Name = "raw_gossip")] bool rawGossip = false,
        [FromQuery(Name = "timestamp")] string? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        if (!ShortChannelId.TryParse(scid, out var parsedScid))
            return Problem($"Invalid short channel id: '{scid}'.", statusCode: StatusCodes.Status400BadRequest);
        if (!QueryHelpers.TryParseTime(timestamp, out var time))
            return Problem($"Invalid timestamp: '{timestamp}'.", statusCode: StatusCodes.Status400BadRequest);

        var channel = await _channels.GetChannelAsync(parsedScid.Value, time.AsOf, nodeInformation, rawGossip, cancellationToken);
        if (channel is null)
            return Problem($"Channel '{scid}' not found.", statusCode: StatusCodes.Status404NotFound);

        return Ok(channel.ToDto());
    }

    /// <summary>Channels valid at a timestamp: no timestamp =&gt; all channels, "now" =&gt; currently open, else open at that instant.</summary>
    [HttpGet]
    public async Task<IActionResult> GetChannels(
        [FromQuery(Name = "timestamp")] string? timestamp = null,
        [FromQuery(Name = "limit")] int? limit = null,
        [FromQuery(Name = "offset")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        if (!QueryHelpers.TryParseTime(timestamp, out var time))
            return Problem($"Invalid timestamp: '{timestamp}'.", statusCode: StatusCodes.Status400BadRequest);

        var (clampedLimit, clampedOffset) = QueryHelpers.ClampPage(limit, offset);
        var filter = new ChannelListFilter(time.At, time.IsNow, IncludeRawGossip: false, clampedLimit, clampedOffset);

        var page = await _channels.GetChannelsAsync(filter, cancellationToken);
        return Ok(page.ToDto(c => c.ToDto()));
    }

    /// <summary>
    /// scid + capacity_sat for every channel open at a timestamp (no timestamp = all channels,
    /// "now" = currently open). Lightweight companion to the snapshot for reconstructing the graph,
    /// since channel_announcement gossip carries no capacity.
    /// </summary>
    [HttpGet("capacities")]
    public async Task<IActionResult> GetChannelCapacities(
        [FromQuery(Name = "timestamp")] string? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        if (!QueryHelpers.TryParseTime(timestamp, out var time))
            return Problem($"Invalid timestamp: '{timestamp}'.", statusCode: StatusCodes.Status400BadRequest);

        var capacities = await _channels.GetCapacitiesAsync(time.At, time.IsNow, cancellationToken);
        return Ok(capacities.Select(c => c.ToDto()));
    }

    /// <summary>Channel update history. raw=true returns concatenated raw gossip; raw=false the update chain.</summary>
    [HttpGet("{scid}/history")]
    public async Task<IActionResult> GetChannelHistory(
        string scid,
        [FromQuery(Name = "raw")] bool raw = false,
        [FromQuery(Name = "timestamp")] string? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        if (!ShortChannelId.TryParse(scid, out var parsedScid))
            return Problem($"Invalid short channel id: '{scid}'.", statusCode: StatusCodes.Status400BadRequest);
        if (!QueryHelpers.TryParseTime(timestamp, out var time))
            return Problem($"Invalid timestamp: '{timestamp}'.", statusCode: StatusCodes.Status400BadRequest);

        var until = time.AsOf;

        if (raw)
        {
            var bytes = await _channels.GetChannelHistoryRawAsync(parsedScid.Value, until, cancellationToken);
            return File(bytes, "application/octet-stream");
        }

        var updates = await _channels.GetUpdateHistoryAsync(parsedScid.Value, until, includeRawGossip: false, cancellationToken);
        return Ok(updates.Select(u => u.ToDto()));
    }
}

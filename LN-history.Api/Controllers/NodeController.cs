using Asp.Versioning;
using LN_history.Api.Infrastructure;
using LN_history.Api.Mapping;
using LN_history.Core.Services;
using LN_History.Model;
using Microsoft.AspNetCore.Mvc;

namespace LN_history.Api.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("ln-history/v{v:apiVersion}/nodes")]
public class NodeController : ControllerBase
{
    private readonly INodeService _nodes;
    private readonly IChannelService _channels;

    public NodeController(INodeService nodes, IChannelService channels)
    {
        _nodes = nodes;
        _channels = channels;
    }

    /// <summary>Single node by pubkey. channelCount=all also computes the all-time channel degree.</summary>
    [HttpGet("{nodeId}")]
    public async Task<IActionResult> GetNode(
        string nodeId,
        [FromQuery(Name = "raw_gossip")] bool rawGossip = false,
        [FromQuery(Name = "timestamp")] string? timestamp = null,
        [FromQuery(Name = "channelCount")] string? channelCount = null,
        CancellationToken cancellationToken = default)
    {
        if (!QueryHelpers.TryParseTime(timestamp, out var time))
            return Problem($"Invalid timestamp: '{timestamp}'.", statusCode: StatusCodes.Status400BadRequest);

        var includeAllTimeDegree = string.Equals(channelCount, "all", StringComparison.OrdinalIgnoreCase);

        var node = await _nodes.GetNodeAsync(nodeId, time.AsOf, includeAllTimeDegree, rawGossip, cancellationToken);
        if (node is null)
            return Problem($"Node '{nodeId}' not found.", statusCode: StatusCodes.Status404NotFound);

        return Ok(node.ToDto());
    }

    /// <summary>Nodes present at a timestamp: no timestamp =&gt; all nodes, otherwise nodes that existed at that instant.</summary>
    [HttpGet]
    public async Task<IActionResult> GetNodes(
        [FromQuery(Name = "timestamp")] string? timestamp = null,
        [FromQuery(Name = "limit")] int? limit = null,
        [FromQuery(Name = "offset")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        if (!QueryHelpers.TryParseTime(timestamp, out var time))
            return Problem($"Invalid timestamp: '{timestamp}'.", statusCode: StatusCodes.Status400BadRequest);

        var (clampedLimit, clampedOffset) = QueryHelpers.ClampPage(limit, offset);
        var page = await _nodes.GetNodesAsync(time.At, time.IsNow, clampedLimit, clampedOffset, cancellationToken);
        return Ok(page.ToDto(n => n.ToDto()));
    }

    /// <summary>Node announcement history. raw=true returns concatenated raw gossip; raw=false the node with its full announcement chain.</summary>
    [HttpGet("{nodeId}/history")]
    public async Task<IActionResult> GetNodeHistory(
        string nodeId,
        [FromQuery(Name = "raw")] bool raw = false,
        [FromQuery(Name = "timestamp")] string? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        if (!QueryHelpers.TryParseTime(timestamp, out var time))
            return Problem($"Invalid timestamp: '{timestamp}'.", statusCode: StatusCodes.Status400BadRequest);

        var until = time.AsOf;

        if (raw)
        {
            var bytes = await _nodes.GetNodeHistoryRawAsync(nodeId, until, cancellationToken);
            return File(bytes, "application/octet-stream");
        }

        var node = await _nodes.GetNodeHistoryAsync(nodeId, until, includeRawGossip: false, cancellationToken);
        if (node is null)
            return Problem($"Node '{nodeId}' not found.", statusCode: StatusCodes.Status404NotFound);

        return Ok(node.ToDto());
    }

    /// <summary>Channels a node participates in (as source or target).</summary>
    [HttpGet("{nodeId}/channels")]
    public async Task<IActionResult> GetNodeChannels(
        string nodeId,
        [FromQuery(Name = "raw_gossip")] bool rawGossip = false,
        [FromQuery(Name = "timestamp")] string? timestamp = null,
        [FromQuery(Name = "limit")] int? limit = null,
        [FromQuery(Name = "offset")] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        if (!QueryHelpers.TryParseTime(timestamp, out var time))
            return Problem($"Invalid timestamp: '{timestamp}'.", statusCode: StatusCodes.Status400BadRequest);

        var (clampedLimit, clampedOffset) = QueryHelpers.ClampPage(limit, offset);
        var filter = new ChannelListFilter(time.At, time.IsNow, rawGossip, clampedLimit, clampedOffset);

        var page = await _channels.GetChannelsByNodeAsync(nodeId, filter, cancellationToken);
        return Ok(page.ToDto(c => c.ToDto()));
    }
}

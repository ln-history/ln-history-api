using Asp.Versioning;
using Dapper;
using LN_history.Api.Instrumentation;
using LN_history.Api.Model; // Assume you have DTOs here
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace LN_history.Api.v1.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("ln-history/v{v:apiVersion}/nodes")]
public class NodeController : ControllerBase
{
    private readonly NpgsqlConnection _dbConnection;
    private readonly AppMetrics _metrics;

    public NodeController(NpgsqlConnection dbConnection, AppMetrics metrics)
    {
        _dbConnection = dbConnection;
        _metrics = metrics;
    }

    /// <summary>
    /// Gets all raw node_announcements for a specific Node ID over its entire history.
    /// </summary>
    [HttpGet("{nodeId}/gossip")]
    public async Task<IActionResult> GetNodeGossipHistory(string nodeId)
    {
        // Default to full history
        var queryTimer = System.Diagnostics.Stopwatch.StartNew();
        var result = await GetNodeGossipHistoryRange(nodeId, DateTime.MinValue, DateTime.MaxValue);
        queryTimer.Stop();
        _metrics.DbQueryDuration.Record(queryTimer.Elapsed.TotalSeconds);
        
        return Ok(result);
    }

    /// <summary>
    /// Gets raw node_announcements for a Node ID within a specific time range.
    /// </summary>
    [HttpGet("{nodeId}/gossip/range")]
    public async Task<IActionResult> GetNodeGossipHistoryRange(string nodeId, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var queryTimer = System.Diagnostics.Stopwatch.StartNew();
        
        await _dbConnection.OpenAsync();

        var sql = """
            SELECT raw_gossip 
            FROM node_announcements
            WHERE node_id = @nodeId
              AND valid_from >= @from
              AND valid_from <= @to
            ORDER BY valid_from ASC
        """;

        var result = await _dbConnection.QueryAsync<byte[]>(sql, new { nodeId, from, to });
        queryTimer.Stop();
        _metrics.DbQueryDuration.Record(queryTimer.Elapsed.TotalSeconds);
        
        return Ok(result);
    }

    /// <summary>
    /// Gets the most recent parsed information for a node (Current State).
    /// </summary>
    [HttpGet("{nodeId}")]
    public async Task<IActionResult> GetCurrentNodeInfo(string nodeId)
    {
        var queryTimer = System.Diagnostics.Stopwatch.StartNew();
        var result = await GetNodeInfoAtTime(nodeId, DateTime.UtcNow);
        queryTimer.Stop();
        _metrics.DbQueryDuration.Record(queryTimer.Elapsed.TotalSeconds);
        
        return Ok(result);
    }

    /// <summary>
    /// Time Travel: Gets the node information as it appeared at a specific point in time.
    /// </summary>
    [HttpGet("{nodeId}/history/{timestamp}")]
    public async Task<IActionResult> GetNodeInfoAtTime(string nodeId, DateTime timestamp)
    {
        var queryTimer = System.Diagnostics.Stopwatch.StartNew();
        await _dbConnection.OpenAsync();

        // Join with addresses to get full picture
        var sql = """
            SELECT 
                n.node_id, 
                n.first_seen, 
                n.last_seen,
                na.alias,
                na.rgb_color,
                na.features,
                na.valid_from as last_updated
            FROM nodes n
            JOIN node_announcements na ON n.node_id = na.node_id
            WHERE n.node_id = @nodeId
              -- SCD Type 2 Logic: Find the single active record at Time T
              AND na.valid_from <= @timestamp 
              AND (na.valid_to > @timestamp OR na.valid_to IS NULL)
        """;

        var nodeInfo = await _dbConnection.QuerySingleOrDefaultAsync<NodeDto>(sql, new { nodeId, timestamp });

        if (nodeInfo == null) return NotFound("Node not found or not active at this timestamp.");

        // Fetch Addresses valid for this announcement
        // (Note: Addresses are linked to the specific announcement via gossip_id)
        var addrSql = """
            SELECT t.name as type, na.address, na.port
            FROM node_addresses na
            JOIN address_types t ON na.type_id = t.id
            JOIN node_announcements ann ON na.gossip_id = ann.gossip_id
            WHERE ann.node_id = @nodeId
              AND ann.valid_from <= @timestamp 
              AND (ann.valid_to > @timestamp OR ann.valid_to IS NULL)
        """;
        
        queryTimer.Stop();
        _metrics.DbQueryDuration.Record(queryTimer.Elapsed.TotalSeconds);
        
        nodeInfo.Addresses = (await _dbConnection.QueryAsync<NodeAddressDto>(addrSql, new { nodeId, timestamp })).ToList();

        return Ok(nodeInfo);
    }
}
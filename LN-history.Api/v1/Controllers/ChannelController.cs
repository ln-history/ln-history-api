using Asp.Versioning;
using Dapper;
using LN_history.Api.Instrumentation;
using LN_history.Api.Model;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace LN_history.Api.v1.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("ln-history/v{v:apiVersion}/channels")]
public class ChannelController : ControllerBase
{
    private readonly NpgsqlConnection _dbConnection;
    private readonly AppMetrics _metrics;

    public ChannelController(NpgsqlConnection dbConnection, AppMetrics metrics)
    {
        _dbConnection = dbConnection;
        _metrics = metrics;
    }

    // --- RAW GOSSIP ---

    [HttpGet("{scid}/gossip")]
    public async Task<IActionResult> GetChannelGossip(string scid)
    {
        return await GetChannelGossipRange(scid, DateTime.MinValue, DateTime.MaxValue);
    }

    [HttpGet("{scid}/gossip/range")]
    public async Task<IActionResult> GetChannelGossipRange(string scid, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        long scidInt = ParseScid(scid);
        if (scidInt == 0) return BadRequest("Invalid SCID format");

        var queryTimer = System.Diagnostics.Stopwatch.StartNew();
        await _dbConnection.OpenAsync();

        // Combines Channel Announcement + All Updates
        var sql = """
            SELECT raw_gossip, 'announcement' as type, NULL as valid_from
            FROM channels 
            WHERE scid = @scidInt
            
            UNION ALL
            
            SELECT raw_gossip, 'update' as type, valid_from
            FROM channel_updates
            WHERE scid = @scidInt
              AND valid_from >= @from
              AND valid_from <= @to
            ORDER BY valid_from ASC
        """;

        var result = await _dbConnection.QueryAsync(sql, new { scidInt, from, to });
        queryTimer.Stop();
        _metrics.DbQueryDuration.Record(queryTimer.Elapsed.TotalSeconds);
        
        return Ok(result);
    }

    // --- PARSED INFO (Time Travel) ---

    [HttpGet("{scid}/info/{timestamp}")]
    public async Task<IActionResult> GetChannelInfoAtTime(string scid, DateTime timestamp)
    {
        long scidInt = ParseScid(scid);
        if (scidInt == 0) return BadRequest("Invalid SCID format");
        
        var queryTimer = System.Diagnostics.Stopwatch.StartNew();
        await _dbConnection.OpenAsync();

        // 1. Get Static Channel Data
        var chanSql = """
            SELECT scid, source_node_id, target_node_id, capacity_sat, funding_timestamp, closing_timestamp
            FROM channels
            WHERE scid = @scidInt
        """;
        var channel = await _dbConnection.QuerySingleOrDefaultAsync<ChannelDto>(chanSql, new { scidInt });
        
        if (channel == null) return NotFound("Channel not found.");

        // 2. Get Active Updates (Policies) at Time T
        var updateSql = """
            SELECT direction::int::bool as direction,, fee_base_msat, fee_proportional_millionths, htlc_minimum_msat, htlc_maximum_msat, valid_from
            FROM channel_updates
            WHERE scid = @scidInt
              AND valid_from <= @timestamp
              AND (valid_to > @timestamp OR valid_to IS NULL)
        """;
        
        channel.Policies = (await _dbConnection.QueryAsync<ChannelPolicyDto>(updateSql, new { scidInt, timestamp })).ToList();

        queryTimer.Stop();
        _metrics.DbQueryDuration.Record(queryTimer.Elapsed.TotalSeconds);
        
        return Ok(channel);
    }

    // --- STATS ---

    [HttpGet("{scid}/update-count")]
    public async Task<IActionResult> GetUpdateCount(string scid)
    {
        long scidInt = ParseScid(scid);
        var queryTimer = System.Diagnostics.Stopwatch.StartNew();
        await _dbConnection.OpenAsync();

        var count = await _dbConnection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM channel_updates WHERE scid = @scidInt", 
            new { scidInt }
        );
        
        queryTimer.Stop();
        _metrics.DbQueryDuration.Record(queryTimer.Elapsed.TotalSeconds);
        return Ok(new { scid, count });
    }

    // Helper to handle "800x1x1" vs raw int
    private long ParseScid(string input)
    {
        if (long.TryParse(input, out var result)) return result;
        if (input.Contains('x'))
        {
            try
            {
                var parts = input.Split('x').Select(long.Parse).ToArray();
                return (parts[0] << 40) | (parts[1] << 16) | parts[2];
            }
            catch { return 0; }
        }
        return 0;
    }
}
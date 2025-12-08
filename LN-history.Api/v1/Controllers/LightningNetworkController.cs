using Asp.Versioning;
using LN_history.Api.Instrumentation; // Ensure this namespace is imported
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LN_history.Api.v1.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("ln-history/v{v:apiVersion}/[controller]")]
public class LightningNetworkController : ControllerBase
{
    private readonly ILogger<LightningNetworkController> _logger;
    private readonly NpgsqlConnection _dbConnection;
    private readonly AppMetrics _metrics; // Inject Custom Metrics

    public LightningNetworkController(
        ILogger<LightningNetworkController> logger,
        NpgsqlConnection dbConnection,
        AppMetrics metrics)
    {
        _logger = logger;
        _dbConnection = dbConnection;
        _metrics = metrics;
    }

    [HttpGet("snapshot/{timestamp}/stream")]
    public async Task<IActionResult> GetSnapshotStream(DateTime timestamp, CancellationToken cancellationToken)
    {
        var totalTimer = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Starting GetSnapshotStream for timestamp: {Timestamp}", timestamp);

        try
        {
            await _dbConnection.OpenAsync(cancellationToken);
            
            Response.ContentType = "application/octet-stream";
            Response.Headers.Add("Content-Disposition", $"attachment; filename=snapshot_{timestamp:yyyyMMdd_HHmmss}.bin");

            // We execute 3 separate queries sequentially.
            // This starts streaming data immediately without waiting for a huge UNION result to materialize.
            var queries = new[]
            {
                // 1. Active Node Announcements at Time T
                """
                SELECT raw_gossip 
                FROM node_announcements 
                WHERE valid_from <= @timestamp 
                  AND (valid_to > @timestamp OR valid_to IS NULL)
                """,
                
                // 2. Active Channels at Time T (Funded before T, Closed after T or never)
                """
                SELECT raw_gossip 
                FROM channels 
                WHERE funding_timestamp <= @timestamp 
                  AND (closing_timestamp > @timestamp OR closing_timestamp IS NULL)
                """,

                // 3. Active Channel Updates at Time T
                """
                SELECT raw_gossip 
                FROM channel_updates 
                WHERE valid_from <= @timestamp 
                  AND (valid_to > @timestamp OR valid_to IS NULL)
                """
            };

            long totalBytes = 0;
            var queryTimer = new System.Diagnostics.Stopwatch();
            var streamTimer = new System.Diagnostics.Stopwatch();

            foreach (var sql in queries)
            {
                // Measure Query Planning/Execution Start
                queryTimer.Restart();
                using var cmd = new NpgsqlCommand(sql, _dbConnection);
                cmd.Parameters.AddWithValue("@timestamp", timestamp);
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                queryTimer.Stop();
                _metrics.DbQueryDuration.Record(queryTimer.Elapsed.TotalSeconds);

                // Measure Streaming
                streamTimer.Start();
                while (await reader.ReadAsync(cancellationToken))
                {
                    if (!reader.IsDBNull(0))
                    {
                        var bytes = (byte[])reader[0];
                        await Response.Body.WriteAsync(bytes, cancellationToken);
                        totalBytes += bytes.Length;
                    }
                }
                streamTimer.Stop();
            }

            // Record total streaming time across all loops
            _metrics.StreamingDuration.Record(streamTimer.Elapsed.TotalSeconds);

            totalTimer.Stop();
            _metrics.SnapshotGenerationDuration.Record(totalTimer.Elapsed.TotalSeconds);

            _logger.LogInformation("Streamed {Bytes} bytes in {ElapsedMs}ms", totalBytes, totalTimer.ElapsedMilliseconds);

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot stream failed");
            throw;
        }
    }

    [HttpGet("snapshot/{timestamp}/copy")]
    public async Task<IActionResult> GetSnapshotCopy(DateTime timestamp, CancellationToken cancellationToken)
    {
        var totalTimer = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Starting COPY Snapshot for: {Timestamp}", timestamp);

        try
        {
            await _dbConnection.OpenAsync(cancellationToken);
            var timestampStr = timestamp.ToString("yyyy-MM-dd HH:mm:ss");

            // Use the native Postgres COPY protocol for maximum throughput
            // Note: UNION ALL is required here as COPY takes a single query
            var copySql = $"""
                           COPY (
                               SELECT raw_gossip 
                               FROM node_announcements 
                               WHERE valid_from <= '{timestampStr}'::timestamp 
                                 AND (valid_to > '{timestampStr}'::timestamp OR valid_to IS NULL)

                               UNION ALL

                               SELECT raw_gossip 
                               FROM channels 
                               WHERE funding_timestamp <= '{timestampStr}'::timestamp 
                                 AND (closing_timestamp > '{timestampStr}'::timestamp OR closing_timestamp IS NULL)

                               UNION ALL

                               SELECT raw_gossip 
                               FROM channel_updates 
                               WHERE valid_from <= '{timestampStr}'::timestamp 
                                 AND (valid_to > '{timestampStr}'::timestamp OR valid_to IS NULL)
                                 
                           ) TO STDOUT (FORMAT BINARY)
                           """;

            Response.ContentType = "application/octet-stream";
            Response.Headers.Add("Content-Disposition", $"attachment; filename=snapshot_{timestamp:yyyyMMdd_HHmmss}.bin");

            // 1. Measure "Time to First Byte" (Database prep time)
            var queryTimer = System.Diagnostics.Stopwatch.StartNew();
            await using var copyStream = await _dbConnection.BeginRawBinaryCopyAsync(copySql, cancellationToken);
            queryTimer.Stop();
            _metrics.DbQueryDuration.Record(queryTimer.Elapsed.TotalSeconds);

            // 2. Measure Streaming Time
            var streamTimer = System.Diagnostics.Stopwatch.StartNew();
            await copyStream.CopyToAsync(Response.Body, cancellationToken);
            streamTimer.Stop();
            _metrics.StreamingDuration.Record(streamTimer.Elapsed.TotalSeconds);

            totalTimer.Stop();
            _metrics.SnapshotGenerationDuration.Record(totalTimer.Elapsed.TotalSeconds);

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "COPY Snapshot failed");
            throw;
        }
    }

    [HttpGet("snapshot/diff/{startTimestamp}/{endTimestamp}/copy")]
    public async Task<IActionResult> GetSnapshotDiffCopy(DateTime startTimestamp, DateTime endTimestamp, CancellationToken cancellationToken)
    {
        var totalTimer = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Starting COPY Diff for: {Start} to {End}", startTimestamp, endTimestamp);

        try
        {
            await _dbConnection.OpenAsync(cancellationToken);
            var startStr = startTimestamp.ToString("yyyy-MM-dd HH:mm:ss");
            var endStr = endTimestamp.ToString("yyyy-MM-dd HH:mm:ss");

            // Logic: Get messages that were created/valid within this window.
            // Using 'valid_from' ensures we catch new updates/nodes.
            // Using 'funding_timestamp' ensures we catch new channels.
            var copySql = $"""
                           COPY (
                               SELECT raw_gossip 
                               FROM node_announcements 
                               WHERE valid_from BETWEEN '{startStr}'::timestamp AND '{endStr}'::timestamp

                               UNION ALL

                               SELECT raw_gossip 
                               FROM channels 
                               WHERE funding_timestamp BETWEEN '{startStr}'::timestamp AND '{endStr}'::timestamp

                               UNION ALL

                               SELECT raw_gossip 
                               FROM channel_updates 
                               WHERE valid_from BETWEEN '{startStr}'::timestamp AND '{endStr}'::timestamp
                               
                           ) TO STDOUT (FORMAT BINARY)
                           """;

            Response.ContentType = "application/octet-stream";
            Response.Headers.Add("Content-Disposition", $"attachment; filename=diff_{startTimestamp:yyyyMMdd}-{endTimestamp:yyyyMMdd}.bin");

            // 1. Measure DB Prep
            var queryTimer = System.Diagnostics.Stopwatch.StartNew();
            await using var copyStream = await _dbConnection.BeginRawBinaryCopyAsync(copySql, cancellationToken);
            queryTimer.Stop();
            _metrics.DbQueryDuration.Record(queryTimer.Elapsed.TotalSeconds);

            // 2. Measure Streaming
            var streamTimer = System.Diagnostics.Stopwatch.StartNew();
            await copyStream.CopyToAsync(Response.Body, cancellationToken);
            streamTimer.Stop();
            _metrics.StreamingDuration.Record(streamTimer.Elapsed.TotalSeconds);

            totalTimer.Stop();
            _metrics.SnapshotGenerationDuration.Record(totalTimer.Elapsed.TotalSeconds);

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "COPY Diff failed");
            throw;
        }
    }
    
    /// <summary>
    /// Not ready yet
    /// </summary>
    /// <param name="timestamp"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    [HttpGet("snapshot/{timestamp}/stream/gist")]
    public async Task<IActionResult> GetSnapshotStreamGiST(DateTime timestamp, CancellationToken cancellationToken)
    {
        var totalTimer = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Starting GetSnapshotStreamGiST (Optimized) for timestamp: {Timestamp}", timestamp);

        try
        {
            await _dbConnection.OpenAsync(cancellationToken);

            Response.ContentType = "application/octet-stream";
            Response.Headers.Add("Content-Disposition", $"attachment; filename=snapshot_gist_{timestamp:yyyyMMdd_HHmmss}.bin");

            // Optimized Query using Postgres Range Types (requires btree_gist extension)
            var sql = """
                      SELECT nrg.raw_gossip
                      FROM node_announcements nrg
                      -- Efficient Range Query: "Does the validity range contain the target timestamp?"
                      WHERE tstzrange(valid_from, COALESCE(valid_to, 'infinity'), '[)') @> @timestamp

                      UNION ALL

                      SELECT c.raw_gossip
                      FROM channels c
                      WHERE tstzrange(funding_timestamp, COALESCE(closing_timestamp, 'infinity'), '[)') @> @timestamp

                      UNION ALL

                      SELECT cu.raw_gossip
                      FROM channel_updates cu
                      WHERE tstzrange(valid_from, COALESCE(valid_to, 'infinity'), '[)') @> @timestamp
                      """;

            long totalBytes = 0;
            
            // 1. Measure Query Execution (Planning + First Byte)
            var queryTimer = System.Diagnostics.Stopwatch.StartNew();
            
            using var cmd = new NpgsqlCommand(sql, _dbConnection);
            cmd.Parameters.AddWithValue("@timestamp", timestamp);
            
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            
            queryTimer.Stop();
            _metrics.DbQueryDuration.Record(queryTimer.Elapsed.TotalSeconds);

            // 2. Measure Data Streaming
            var streamTimer = System.Diagnostics.Stopwatch.StartNew();
            
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    var bytes = (byte[])reader[0];
                    await Response.Body.WriteAsync(bytes, cancellationToken);
                    totalBytes += bytes.Length;
                }
            }
            
            streamTimer.Stop();
            _metrics.StreamingDuration.Record(streamTimer.Elapsed.TotalSeconds);

            totalTimer.Stop();
            _metrics.SnapshotGenerationDuration.Record(totalTimer.Elapsed.TotalSeconds);

            _logger.LogInformation("GiST Streamed {Bytes} bytes in {TotalMs}ms (Query: {QueryMs}ms, Stream: {StreamMs}ms)", 
                totalBytes, totalTimer.ElapsedMilliseconds, queryTimer.ElapsedMilliseconds, streamTimer.ElapsedMilliseconds);

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetSnapshotStreamGiST failed");
            throw;
        }
    }
}
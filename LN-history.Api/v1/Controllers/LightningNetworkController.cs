using Asp.Versioning;
using LN_history.Data.DataStores;
using Microsoft.AspNetCore.Http;
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
    
    private readonly INetworkSnapshotDataStore _networkSnapshotDataStore;
    private readonly NpgsqlConnection _dbConnection;

    public LightningNetworkController(ILogger<LightningNetworkController> logger, INetworkSnapshotDataStore networkSnapshotDataStore, NpgsqlConnection dbConnection)
    {
        _logger = logger;
        
        _networkSnapshotDataStore = networkSnapshotDataStore;
        _dbConnection = dbConnection;
    }
    
    [HttpGet("snapshot/{timestamp}/stream")]
    public async Task<IActionResult> GetSnapshotStream(DateTime timestamp, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Starting GetSnapshotStream for timestamp: {Timestamp}", timestamp);
        
        try
        {
            // Measure database connection time
            var connectionStopwatch = System.Diagnostics.Stopwatch.StartNew();
            await _dbConnection.OpenAsync(cancellationToken);
            connectionStopwatch.Stop();
            _logger.LogInformation("Database connection opened in {ElapsedMs}ms", connectionStopwatch.ElapsedMilliseconds);

            var sql = """
                SELECT nrg.raw_gossip
                FROM nodes n
                JOIN nodes_raw_gossip nrg ON n.node_id_str = nrg.node_id
                WHERE @timestamp BETWEEN n.from_timestamp AND n.last_seen
                  AND nrg.timestamp <= @timestamp
                  AND nrg.timestamp >= @timestamp - INTERVAL '14 days'
                
                UNION ALL
                
                SELECT c.raw_gossip
                FROM channels c
                WHERE @timestamp BETWEEN c.from_timestamp AND c.to_timestamp
                
                UNION ALL
                
                SELECT cu.raw_gossip
                FROM channel_updates cu
                WHERE @timestamp BETWEEN cu.from_timestamp AND cu.to_timestamp
            """; 

            Response.ContentType = "application/octet-stream";
            Response.Headers.Add("Content-Disposition", 
                $"attachment; filename=snapshot_{timestamp:yyyyMMdd_HHmmss}.bin");

            using var cmd = new NpgsqlCommand(sql, _dbConnection);
            cmd.Parameters.AddWithValue("@timestamp", timestamp);

            // Measure query execution time
            var queryStopwatch = System.Diagnostics.Stopwatch.StartNew();
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            queryStopwatch.Stop();
            _logger.LogInformation("Database query executed in {ElapsedMs}ms", queryStopwatch.ElapsedMilliseconds);

            // Measure data streaming time
            var streamingStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var bytesWritten = 0L;
            var recordCount = 0;

            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    var bytes = (byte[])reader[0];
                    await Response.Body.WriteAsync(bytes, cancellationToken);
                    bytesWritten += bytes.Length;
                    recordCount++;
                }
            }
            
            streamingStopwatch.Stop();
            _logger.LogInformation("Data streaming completed in {ElapsedMs}ms, streamed {BytesWritten} bytes from {RecordCount} records", 
                streamingStopwatch.ElapsedMilliseconds, bytesWritten, recordCount);

            stopwatch.Stop();
            _logger.LogInformation("GetSnapshotStream completed successfully in {TotalElapsedMs}ms. Breakdown - Connection: {ConnectionMs}ms, Query: {QueryMs}ms, Streaming: {StreamingMs}ms",
                stopwatch.ElapsedMilliseconds, connectionStopwatch.ElapsedMilliseconds, queryStopwatch.ElapsedMilliseconds, streamingStopwatch.ElapsedMilliseconds);

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "GetSnapshotStream failed after {ElapsedMs}ms for timestamp: {Timestamp}", 
                stopwatch.ElapsedMilliseconds, timestamp);
            throw;
        }
    }
    
    [HttpGet("snapshot/{timestamp}")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSnapshotByTimestamp(
        DateTime timestamp,
        CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Starting GetSnapshotByTimestamp for timestamp: {Timestamp}", timestamp);
        
        try
        {
            // Measure datastore query time
            var datastoreStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var bytes = await _networkSnapshotDataStore.GetSnapshotAsync(timestamp, cancellationToken: cancellationToken);
            datastoreStopwatch.Stop();
            _logger.LogInformation("Datastore query completed in {ElapsedMs}ms, returned {ByteCount} bytes", 
                datastoreStopwatch.ElapsedMilliseconds, bytes?.Length ?? 0);
            
            var fileName = $"snapshot_{timestamp:yyyyMMdd_HHmmss}.bin";
            if (bytes == null || bytes.Length == 0)
            {
                stopwatch.Stop();
                _logger.LogWarning("GetSnapshotByTimestamp completed with no data after {ElapsedMs}ms for timestamp: {Timestamp}", 
                    stopwatch.ElapsedMilliseconds, timestamp);
                return NotFound("No snapshot data available for the given timestamp.");
            }

            // Measure file result creation time
            var fileResultStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = File(bytes, "application/octet-stream", fileName);
            fileResultStopwatch.Stop();
            _logger.LogInformation("File result creation completed in {ElapsedMs}ms", fileResultStopwatch.ElapsedMilliseconds);

            stopwatch.Stop();
            _logger.LogInformation("GetSnapshotByTimestamp completed successfully in {TotalElapsedMs}ms. Breakdown - Datastore: {DatastoreMs}ms, FileResult: {FileResultMs}ms",
                stopwatch.ElapsedMilliseconds, datastoreStopwatch.ElapsedMilliseconds, fileResultStopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (ArgumentException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "GetSnapshotByTimestamp failed with ArgumentException after {ElapsedMs}ms for timestamp: {Timestamp}", 
                stopwatch.ElapsedMilliseconds, timestamp);
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid parameters provided",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "GetSnapshotByTimestamp failed after {ElapsedMs}ms for timestamp: {Timestamp}", 
                stopwatch.ElapsedMilliseconds, timestamp);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal server error during export",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }
    
    [HttpGet("snapshot/{timestamp}/copy")]
    public async Task<IActionResult> GetSnapshotCopy(DateTime timestamp, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Starting GetSnapshotCopy for timestamp: {Timestamp}", timestamp);

        try
        {
            var connectionStopwatch = System.Diagnostics.Stopwatch.StartNew();
            await _dbConnection.OpenAsync(cancellationToken);
            connectionStopwatch.Stop();
            _logger.LogInformation("Database connection opened in {ElapsedMs}ms", connectionStopwatch.ElapsedMilliseconds);

            // Format timestamp for PostgreSQL
            var timestampStr = timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        
            var copySql = $"""
                            COPY (
                                SELECT nrg.raw_gossip
                                FROM nodes n
                                JOIN nodes_raw_gossip nrg ON n.node_id_str = nrg.node_id
                                WHERE '{timestampStr}'::timestamp BETWEEN n.from_timestamp AND n.last_seen
                                  AND nrg.timestamp <= '{timestampStr}'::timestamp
                                  AND nrg.timestamp >= '{timestampStr}'::timestamp - INTERVAL '14 days'
                            
                                UNION ALL
                            
                                SELECT c.raw_gossip
                                FROM channels c
                                WHERE '{timestampStr}'::timestamp BETWEEN c.from_timestamp AND c.to_timestamp
                            
                                UNION ALL
                            
                                SELECT cu.raw_gossip
                                FROM channel_updates cu
                                WHERE '{timestampStr}'::timestamp BETWEEN cu.from_timestamp AND cu.to_timestamp
                                  
                            ) TO STDOUT (FORMAT BINARY)
                            """;

            // Set response headers before starting the stream
            Response.ContentType = "application/octet-stream";
            Response.Headers.Add("Content-Disposition",
                $"attachment; filename=snapshot_{timestamp:yyyyMMdd_HHmmss}.bin");

            // Use Npgsql's raw binary COPY method to stream directly to response
            await using var copyStream = await _dbConnection.BeginRawBinaryCopyAsync(copySql, cancellationToken);
        
            await copyStream.CopyToAsync(Response.Body, cancellationToken);
        
            await Response.Body.FlushAsync(cancellationToken);

            stopwatch.Stop();
            _logger.LogInformation("GetSnapshotCopy completed in {ElapsedMs}ms", 
                stopwatch.ElapsedMilliseconds);

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "GetSnapshotCopy failed after {ElapsedMs}ms for timestamp: {Timestamp}", 
                stopwatch.ElapsedMilliseconds, timestamp);
            throw;
        }
    }
    
    [HttpGet("snapshot/diff/{startTimestamp}/{endtimestamp}/stream")]
    public async Task<IActionResult> GetSnapshotDiffStream(DateTime startTimestamp, DateTime endTimestamp, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Starting GetSnapshotDiffStream for startTimestamp: {Timestamp}, endTimestamp {Timestamp}", startTimestamp, endTimestamp);
        
        try
        {
            // Measure database connection time
            var connectionStopwatch = System.Diagnostics.Stopwatch.StartNew();
            await _dbConnection.OpenAsync(cancellationToken);
            connectionStopwatch.Stop();
            _logger.LogInformation("Database connection opened in {ElapsedMs}ms", connectionStopwatch.ElapsedMilliseconds);

            var sql = """
                          SELECT nrg.raw_gossip
                          FROM nodes_raw_gossip nrg
                          WHERE nrg.timestamp BETWEEN @startTimestamp AND @endTimestamp
                      
                          UNION ALL
                      
                          SELECT c.raw_gossip
                          FROM channels c
                          WHERE c.from_timestamp <= @endTimestamp
                            AND (c.to_timestamp IS NULL OR c.to_timestamp >= @startTimestamp)
                      
                          UNION ALL
                      
                          SELECT cu.raw_gossip
                          FROM channel_updates cu
                          WHERE @timestamp BETWEEN cu.from_timestamp AND cu.to_timestamp
                      """;

            Response.ContentType = "application/octet-stream";
            Response.Headers.Add("Content-Disposition", 
                $"attachment; filename=snapshot_{startTimestamp:yyyyMMdd_HHmmss}.bin");

            using var cmd = new NpgsqlCommand(sql, _dbConnection);
            cmd.Parameters.AddWithValue("@startTimestamp", startTimestamp);
            cmd.Parameters.AddWithValue("@endTimestamp", endTimestamp);

            // Measure query execution time
            var queryStopwatch = System.Diagnostics.Stopwatch.StartNew();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            queryStopwatch.Stop();
            _logger.LogInformation("Database query executed in {ElapsedMs}ms", queryStopwatch.ElapsedMilliseconds);

            // Measure data streaming time
            var streamingStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var bytesWritten = 0L;
            var recordCount = 0;

            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    var bytes = (byte[])reader[0];
                    await Response.Body.WriteAsync(bytes, cancellationToken);
                    bytesWritten += bytes.Length;
                    recordCount++;
                }
            }
            
            streamingStopwatch.Stop();
            _logger.LogInformation("Data streaming completed in {ElapsedMs}ms, streamed {BytesWritten} bytes from {RecordCount} records", 
                streamingStopwatch.ElapsedMilliseconds, bytesWritten, recordCount);

            stopwatch.Stop();
            _logger.LogInformation("GetSnapshotDiffStream completed successfully in {TotalElapsedMs}ms. Breakdown - Connection: {ConnectionMs}ms, Query: {QueryMs}ms, Streaming: {StreamingMs}ms",
                stopwatch.ElapsedMilliseconds, connectionStopwatch.ElapsedMilliseconds, queryStopwatch.ElapsedMilliseconds, streamingStopwatch.ElapsedMilliseconds);

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "GetSnapshotDiffStream failed after {ElapsedMs}ms for startTimestamp: {Timestamp}", 
                stopwatch.ElapsedMilliseconds, startTimestamp);
            throw;
        }
    }
    
    [HttpGet("snapshot/diff/{startTimestamp}/{endTimestamp}/copy")]
    public async Task<IActionResult> GetSnapshotDiffCopy(DateTime startTimestamp, DateTime endTimestamp, CancellationToken cancellationToken)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogInformation("Starting GetSnapshotDiffCopy for startTimestamp: {Timestamp}, endTimestamp {Timestamp}", startTimestamp, endTimestamp);
        
        try
        {
            // Measure database connection time
            var connectionStopwatch = System.Diagnostics.Stopwatch.StartNew();
            await _dbConnection.OpenAsync(cancellationToken);
            connectionStopwatch.Stop();
            _logger.LogInformation("Database connection opened in {ElapsedMs}ms", connectionStopwatch.ElapsedMilliseconds);

            // Format timestamp for PostgreSQL
            var startTimestampStr = startTimestamp.ToString("yyyy-MM-dd HH:mm:ss");
            var endTimestampStr = endTimestamp.ToString("yyyy-MM-dd HH:mm:ss");
            
            var copySql = $"""
                           COPY (
                               SELECT nrg.raw_gossip
                               FROM nodes_raw_gossip nrg
                               WHERE nrg.timestamp BETWEEN '{startTimestampStr}'::timestamp AND '{endTimestampStr}'::timestamp
                           
                               UNION ALL
                           
                               SELECT c.raw_gossip
                               FROM channels c
                               WHERE (c.from_timestamp BETWEEN '{startTimestampStr}'::timestamp AND '{endTimestampStr}'::timestamp)
                                 OR (c.to_timestamp BETWEEN '{startTimestampStr}'::timestamp AND '{endTimestampStr}'::timestamp)
                           
                               UNION ALL
                           
                               SELECT cu.raw_gossip
                               FROM channel_updates cu
                               WHERE (cu.from_timestamp BETWEEN '{startTimestampStr}'::timestamp AND '{endTimestampStr}'::timestamp)
                                 OR (cu.to_timestamp BETWEEN '{startTimestampStr}'::timestamp AND '{endTimestampStr}'::timestamp)
                           ) TO STDOUT (FORMAT BINARY)
                           """;

            Response.ContentType = "application/octet-stream";
            Response.Headers.Add("Content-Disposition", 
                $"attachment; filename=snapshot_diff_from-{startTimestamp:yyyyMMdd_HHmmss}_to-{startTimestamp:yyyyMMdd_HHmmss}.bin");

            // Measure query execution time
            var copyStopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            // Use Npgsql's raw binary COPY method to stream directly to response
            await using var copyStream = await _dbConnection.BeginRawBinaryCopyAsync(copySql, cancellationToken);
        
            await copyStream.CopyToAsync(Response.Body, cancellationToken);
        
            await Response.Body.FlushAsync(cancellationToken);

            copyStopwatch.Stop();
            _logger.LogInformation("GetSnapshotCopy completed in {ElapsedMs}ms", 
                copyStopwatch.ElapsedMilliseconds);

            stopwatch.Stop();
            _logger.LogInformation("GetSnapshotDiffCopy completed successfully in {TotalElapsedMs}ms. Breakdown - Connection: {ConnectionMs}ms, Query: {QueryMs}ms",
                stopwatch.ElapsedMilliseconds, connectionStopwatch.ElapsedMilliseconds, copyStopwatch.ElapsedMilliseconds);

            return new EmptyResult();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "GetSnapshotDiffCopy failed after {ElapsedMs}ms for startTimestamp: {Timestamp}", 
                stopwatch.ElapsedMilliseconds, startTimestamp);
            throw;
        }
    }
    
    // [HttpGet("snapshot/{timestamp}/stream")]
    // public async Task<IActionResult> GetSnapshotStream(DateTime timestamp, CancellationToken cancellationToken)
    // {
    //     await _dbConnection.OpenAsync(cancellationToken);
    //
    //     var sql = """
    //         SELECT nrg.raw_gossip
    //         FROM nodes n
    //         JOIN nodes_raw_gossip nrg ON n.node_id_str = nrg.node_id_str
    //         WHERE @timestamp BETWEEN n.from_timestamp AND n.last_seen
    //           AND nrg.timestamp <= @timestamp
    //           AND nrg.timestamp >= @timestamp - INTERVAL '14 days'
    //         
    //         UNION ALL
    //         
    //         SELECT c.raw_gossip
    //         FROM channels c
    //         WHERE @timestamp BETWEEN c.from_timestamp AND c.to_timestamp
    //         
    //         UNION ALL
    //         
    //         SELECT cu.raw_gossip
    //         FROM channel_updates cu
    //         WHERE @timestamp BETWEEN cu.from_timestamp AND cu.to_timestamp
    //     """; 
    //
    //     Response.ContentType = "application/octet-stream";
    //     Response.Headers.Add("Content-Disposition", 
    //         $"attachment; filename=snapshot_{timestamp:yyyyMMdd_HHmmss}.bin");
    //
    //     using var cmd = new NpgsqlCommand(sql, _dbConnection);
    //     cmd.Parameters.AddWithValue("@timestamp", timestamp);
    //
    //     using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
    //
    //     while (await reader.ReadAsync(cancellationToken))
    //     {
    //         if (!reader.IsDBNull(0))
    //         {
    //             var bytes = (byte[])reader[0];
    //             await Response.Body.WriteAsync(bytes, cancellationToken);
    //         }
    //     }
    //
    //     return new EmptyResult();
    // }
    
    // /// <summary>
    // /// Get all raw_gossip necessary to construct the network topology of the Lightning Networks at a specific timestamp
    // /// </summary>
    // /// <remarks>
    // /// raw_gossip is bytes, take a look at client libraries to parse them into something useful.
    // /// </remarks>
    // /// <param name="timestamp"></param>
    // /// <param name="cancellationToken"></param>
    // /// <response code="200">Successfully got raw_gossip</response>
    // /// <response code="400">Invalid parameters provided</response>
    // /// <response code="500">Internal server error when getting raw_gossip data</response>
    // [HttpGet("snapshot/{timestamp}")]
    // [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    // [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    // [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    // public async Task<IActionResult> GetSnapshotByTimestamp(
    //     DateTime timestamp,
    //     CancellationToken cancellationToken)
    // {
    //     try
    //     {
    //         var bytes = await _networkSnapshotDataStore.GetSnapshotAsync(timestamp, cancellationToken: cancellationToken);
    //         
    //         var fileName = $"ln_snapshot_{timestamp:yyyyMMdd_HHmmss}.bin";
    //         if (bytes == null || bytes.Length == 0)
    //             return NotFound("No snapshot data available for the given timestamp.");
    //
    //         return File(bytes, "application/octet-stream", fileName);
    //     }
    //     catch (ArgumentException ex)
    //     {
    //         return BadRequest(new ProblemDetails
    //         {
    //             Title = "Invalid parameters provided",
    //             Detail = ex.Message,
    //             Status = StatusCodes.Status400BadRequest
    //         });
    //     }
    //     catch (Exception ex)
    //     {
    //         return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
    //         {
    //             Title = "Internal server error during export",
    //             Detail = ex.Message,
    //             Status = StatusCodes.Status500InternalServerError
    //         });
    //     }
    // }

    // /// <summary>
    // /// Gets number of nodes in the Lightning Network by timestamp
    // /// </summary>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="int"/></returns>
    // [HttpGet("nodes/count/{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<int>> GetNodeCountByTimestamp(DateTime timestamp, CancellationToken cancellationToken)
    // {
    //     // var result =  await _lightningNetworkService.GetNodeCountByTimestampAsync(timestamp, cancellationToken);
    //     // return Ok(result);
    //     throw new NotImplementedException();
    // }
    //
    // /// <summary>
    // /// Gets number of channels in the Lightning Network by timestamp
    // /// </summary>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="int"/></returns>
    // [HttpGet("edges/count/{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<int>> GetEdgeCountByTimestamp(DateTime timestamp, CancellationToken cancellationToken)
    // {
    //     // var result =  await _lightningNetworkService.GetEdgeCountByTimestampAsync(timestamp, cancellationToken);
    //     // return Ok(result);
    //     throw new NotImplementedException();
    // }
    //
    // /// <summary>
    // /// Gets median transaction cost for a payment of size paymentSizeSat in the Lightning Network by timestamp using a Monte Carlo Simulation
    // /// </summary>
    // /// <param name="paymentSizeSat">paymentSize in satoshis (sats)</param>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="double"/></returns>
    // [HttpGet("simulateMedian/{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<double>> GetMedianTransactionCostByTimestampUsingSimulation(int paymentSizeSat, DateTime timestamp,
    //     CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
    //
    // /// <summary>
    // /// Gets average transaction cost for a payment of size paymentSizeSat in the Lightning Network by timestamp using a Monte Carlo Simulation
    // /// </summary>
    // /// <param name="paymentSizeSat">paymentSize in satoshis (sats)</param>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="double"/></returns>
    // [HttpGet("simulateAverage{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<double>> GetAverageTransactionCostByTimestampUsingSimulation(int paymentSizeSat, DateTime timestamp,
    //     CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
    //
    // /// <summary>
    // /// Gets median transaction cost for a payment of size paymentSizeSat in the Lightning Network by timestamp using a formula
    // /// </summary>
    // /// <param name="paymentSizeSat">paymentSize in satoshis (sats)</param>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="double"/></returns>
    // [HttpGet("calculateMedian/{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<double>> GetMedianTransactionCostByTimestampUsingCalculation(int paymentSizeSat, DateTime timestamp,
    //     CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
    //
    // /// <summary>
    // /// Gets average transaction cost for a payment of size paymentSizeSat in the Lightning Network by timestamp using a formula
    // /// </summary>
    // /// <param name="paymentSizeSat">paymentSize in satoshis (sats)</param>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="double"/></returns>
    // [HttpGet("calculateAverage/{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<double>> GetAverageTransactionCostByTimestampUsingCalculation(int paymentSizeSat, DateTime timestamp,
    //     CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
    //
    // /// <summary>
    // /// Gets the diameter of the Lightning Network by timestamp.
    // /// The diameter is defined as the longest shortest path of a graph
    // /// </summary>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="int"/></returns>
    // [HttpGet("diameter/{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<double>> GetDiameterOfLightningNetworkByTimestamp(DateTime timestamp, CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
    //
    // /// <summary>
    // /// Gets the average shortest path length of the Lightning Network by timestamp.
    // /// </summary>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="int"/></returns>
    // [HttpGet("avgPathLength/{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<double>> GetAveragePathLengthOfLightningNetworkByTimestamp(DateTime timestamp, CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
    //
    // /// <summary>
    // /// Gets the average degree of the nodes in the Lightning Network by timestamp.
    // /// </summary>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="int"/></returns>
    // [HttpGet("avgDegree/{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<double>> GetAverageDegreeOfLightningNetworkByTimestamp(DateTime timestamp, CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
    //
    // /// <summary>
    // /// Gets the global clustering coefficient of the Lightning Network by timestamp.
    // /// The local clustering coefficient of a node quantifies how close its neighbours are to being a clique.
    // /// </summary>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="double"/></returns>
    // [HttpGet("clusteringCoefficient/local/avg/{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<double>> GetAverageLocalClusteringCoefficientOfLightningNetworkByTimestampAsync(DateTime timestamp, CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
    //
    // /// <summary>
    // /// Gets the global clustering coefficient of the Lightning Network by timestamp.
    // /// The global clustering coefficient is defined as the ratio of actual connections between a node’s neighbors to the maximum possible connections between them.
    // /// </summary>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="double"/></returns>
    // [HttpGet("clusteringCoefficient/global/{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<double>> GetGlobalClusteringCoefficientOfLightningNetworkByTimestampAsync(DateTime timestamp, CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
    //
    // /// <summary>
    // /// Gets the density of the Lightning Network by timestamp.
    // /// The density is defined as the ratio of the number of edges (E) to the maximum possible number of edges (M) in a graph.
    // /// In the case of the Lightning Network - an undirected *multi* graph - allowing parallel edges this metric should *not* be taken too seriously.
    // /// </summary>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="double"/></returns>
    // [HttpGet("density/{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<double>> GetDensityOfLightningNetworkByTimestampAsync(DateTime timestamp, CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
    //
    // /// <summary>
    // /// Gets <see cref="NetworkMetrics"/> of the Lightning Network by timestamp.
    // /// </summary>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="double"/></returns>
    // [HttpGet("metrics/{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<double>> GetNetworkMetricsOfLightningNetworkByTimestampAsync(DateTime timestamp, CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
    //
    // /// <summary>
    // /// Gets number of bridges in the Lightning Network by timestamp.
    // /// A bridge is defined as an edge, that if removed the graph is split into two connected components.
    // /// </summary>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="int"/></returns>
    // [HttpGet("bridges/{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<double>> GetNumberOfBridgesInLightningNetworkByTimestamp(DateTime timestamp, CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
    //
    // /// <summary>
    // /// Calculates the centrality of the Lightning Network analytically 
    // /// </summary>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="paymentSizeSat"></param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="int"/></returns>
    // [HttpGet("centrality/analytical/{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<double>> GetCentralityAnalyticallyOfLightningNetworkByTimestamp(DateTime timestamp, int paymentSizeSat, CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
    //
    // /// <summary>
    // /// Calculates the centrality of the Lightning Network empirically using a Monte-Carlo-Simulation  
    // /// </summary>
    // /// <param name="timestamp">timestamp in ISO 8601 format</param>
    // /// <param name="paymentSizeSat"></param>
    // /// <param name="cancellationToken"></param>
    // /// <returns><see cref="int"/></returns>
    // [HttpGet("centrality/empirical/{timestamp}")]
    // [ProducesResponseType(StatusCodes.Status200OK)]
    // public async Task<ActionResult<double>> GetCentralityEmpiricallyOfLightningNetworkByTimestamp(DateTime timestamp, int paymentSizeSat, CancellationToken cancellationToken)
    // {
    //     throw new NotImplementedException();
    // }
}
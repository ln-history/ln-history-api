using Asp.Versioning;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace LN_history.Api.v1.Controllers;

[ApiController]
[ApiVersion(1)]
[Route("ln-history/v{v:apiVersion}/[controller]")]
public class NodeController : ControllerBase
{
    private readonly NpgsqlConnection _npgsqlConnection;

    public NodeController(NpgsqlConnection npgsqlConnection)
    {
        _npgsqlConnection = npgsqlConnection;
    }

    [HttpGet("{nodeId}/info/{timestamp}/stream")]
public async Task<IActionResult> GetNodeInformation(string nodeId, DateTime timestamp, CancellationToken cancellationToken)
{
    await _npgsqlConnection.OpenAsync(cancellationToken);

    var sql = """
        -- Get all nodes_raw_gossip for the node_id up until the given timestamp
        SELECT nrg.raw_gossip
        FROM nodes_raw_gossip nrg
        WHERE nrg.node_id = @nodeId
          AND nrg.timestamp <= @timestamp

        UNION ALL

        -- Get channels where the node is either source or target
        SELECT c.raw_gossip
        FROM channels c
        WHERE (c.source_node_id = @nodeId OR c.target_node_id = @nodeId)
          AND c.from_timestamp <= @timestamp
          AND (c.to_timestamp IS NULL OR c.to_timestamp > @timestamp)

        UNION ALL

        -- Get latest channel_updates for channels involving this node
        SELECT cu.raw_gossip
        FROM channel_updates cu
        WHERE cu.scid IN (
            SELECT c.scid
            FROM channels c
            WHERE (c.source_node_id = @nodeId OR c.target_node_id_str = @nodeId)
              AND c.from_timestamp <= @timestamp
              AND (c.to_timestamp IS NULL OR c.to_timestamp > @timestamp)
        )
        AND cu.from_timestamp <= @timestamp
        AND cu.to_timestamp > @timestamp
    """;

    Response.ContentType = "application/octet-stream";
    Response.Headers.Append("Content-Disposition", 
        $"attachment; filename=node_{nodeId}_{timestamp:yyyyMMdd_HHmmss}.bin");

    using var cmd = new NpgsqlCommand(sql, _npgsqlConnection);
    cmd.Parameters.AddWithValue("@nodeId", nodeId);
    cmd.Parameters.AddWithValue("@timestamp", timestamp);

    using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

    while (await reader.ReadAsync(cancellationToken))
    {
        if (!reader.IsDBNull(0))
        {
            var bytes = (byte[])reader[0];
            await Response.Body.WriteAsync(bytes, cancellationToken);
        }
    }

    return new EmptyResult();
}
}
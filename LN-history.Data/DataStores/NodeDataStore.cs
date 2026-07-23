using Dapper;
using LN_history.Data.Internal;
using LN_History.Model;
using Npgsql;

namespace LN_history.Data.DataStores;

public class NodeDataStore : INodeDataStore
{
    private readonly NpgsqlDataSource _dataSource;

    public NodeDataStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        DapperConfiguration.EnsureConfigured();
    }

    private const string AnnouncementColumns =
        "gossip_id, node_id, alias, rgb_color, features, valid_from, is_data_update, internal_id";

    private sealed class DegreeRow
    {
        public string NodeId { get; set; } = string.Empty;
        public long Cnt { get; set; }
    }

    public async Task<Node?> GetByIdAsync(string nodeId, DateTime? asOf, bool includeAllTimeDegree, bool includeRawGossip, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var nodeRow = await connection.QuerySingleOrDefaultAsync<NodeRow>(new CommandDefinition(
            "SELECT node_id, first_seen, last_seen, announcement_count FROM nodes WHERE node_id = @nodeId",
            new { nodeId }, cancellationToken: cancellationToken));
        if (nodeRow is null) return null;

        var node = nodeRow.ToNode();
        node.NumberOfChannels = await OpenDegreeAsync(connection, nodeId, cancellationToken);
        if (includeAllTimeDegree)
        {
            node.NumberOfChannelsAllTime = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT count(*) FROM channels WHERE source_node_id = @nodeId OR target_node_id = @nodeId",
                new { nodeId }, cancellationToken: cancellationToken));
        }

        var rawColumn = includeRawGossip ? "raw_gossip" : "NULL::bytea AS raw_gossip";
        var announcement = await connection.QuerySingleOrDefaultAsync<NodeAnnouncementRow>(new CommandDefinition(
            $"""
             SELECT {AnnouncementColumns}, {rawColumn}
             FROM node_announcements_complete
             WHERE node_id = @nodeId AND valid_from <= @asOf
             ORDER BY valid_from DESC LIMIT 1
             """,
            new { nodeId, asOf = asOf ?? DateTime.UtcNow }, cancellationToken: cancellationToken));

        if (announcement is not null)
        {
            var addresses = await LoadAddressesAsync(connection, new[] { announcement.InternalId }, cancellationToken);
            node.Announcements = new[] { announcement.ToNodeAnnouncement(addresses.GetValueOrDefault(announcement.InternalId)) };
        }

        return node;
    }

    /// <summary>A node is "currently active" if it was last seen within this window.</summary>
    private const string ActiveWindow = "now() - interval '14 days'";

    public async Task<Page<Node>> GetNodesAsync(DateTime? existedAt, bool currentlyActive, int limit, int offset, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var where = currentlyActive
            ? $"WHERE last_seen >= {ActiveWindow}"
            : existedAt is null
                ? string.Empty
                : "WHERE first_seen <= @existedAt AND last_seen >= @existedAt";
        var parameters = new { existedAt, limit, offset };

        var total = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            $"SELECT count(*) FROM nodes {where}", parameters, cancellationToken: cancellationToken));
        if (total == 0) return Page<Node>.Empty(limit, offset);

        var rows = (await connection.QueryAsync<NodeRow>(new CommandDefinition(
            $"SELECT node_id, first_seen, last_seen, announcement_count FROM nodes {where} ORDER BY node_id LIMIT @limit OFFSET @offset",
            parameters, cancellationToken: cancellationToken))).ToList();

        var degrees = await OpenDegreesAsync(connection, rows.Select(r => r.NodeId).ToArray(), cancellationToken);

        var items = rows.Select(r =>
        {
            var node = r.ToNode();
            node.NumberOfChannels = degrees.GetValueOrDefault(r.NodeId);
            return node;
        }).ToList();

        return new Page<Node>(items, total, limit, offset);
    }

    public async Task<IReadOnlyList<NodeAnnouncement>> GetAnnouncementHistoryAsync(string nodeId, DateTime? until, bool includeRawGossip, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var rawColumn = includeRawGossip ? "raw_gossip" : "NULL::bytea AS raw_gossip";
        var untilClause = until is null ? string.Empty : "AND valid_from <= @until";

        var rows = (await connection.QueryAsync<NodeAnnouncementRow>(new CommandDefinition(
            $"""
             SELECT {AnnouncementColumns}, {rawColumn}
             FROM node_announcements_complete
             WHERE node_id = @nodeId {untilClause}
             ORDER BY valid_from ASC
             """,
            new { nodeId, until }, cancellationToken: cancellationToken))).ToList();
        if (rows.Count == 0) return Array.Empty<NodeAnnouncement>();

        var addresses = await LoadAddressesAsync(connection, rows.Select(r => r.InternalId).ToArray(), cancellationToken);
        return rows.Select(r => r.ToNodeAnnouncement(addresses.GetValueOrDefault(r.InternalId))).ToList();
    }

    public async Task<byte[]> GetHistoryRawAsync(string nodeId, DateTime? until, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var untilClause = until is null ? string.Empty : "AND valid_from <= @until";
        var blobs = await connection.QueryAsync<byte[]>(new CommandDefinition(
            $"""
             SELECT raw_gossip FROM node_announcements_complete
             WHERE node_id = @nodeId AND raw_gossip IS NOT NULL {untilClause}
             ORDER BY valid_from ASC
             """,
            new { nodeId, until }, cancellationToken: cancellationToken));
        return GossipBytes.Concat(blobs);
    }

    public async Task<bool> ExistsAsync(string nodeId, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS(SELECT 1 FROM nodes WHERE node_id = @nodeId)", new { nodeId }, cancellationToken: cancellationToken));
    }

    private static async Task<int> OpenDegreeAsync(NpgsqlConnection connection, string nodeId, CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM channels WHERE (source_node_id = @nodeId OR target_node_id = @nodeId) AND closing_timestamp IS NULL",
            new { nodeId }, cancellationToken: cancellationToken));

    private static async Task<Dictionary<string, int>> OpenDegreesAsync(NpgsqlConnection connection, string[] nodeIds, CancellationToken cancellationToken)
    {
        if (nodeIds.Length == 0) return new Dictionary<string, int>();

        const string sql = """
            SELECT node_id, count(*)::int AS cnt FROM (
                SELECT source_node_id AS node_id FROM channels WHERE closing_timestamp IS NULL AND source_node_id = ANY(@nodeIds)
                UNION ALL
                SELECT target_node_id AS node_id FROM channels WHERE closing_timestamp IS NULL AND target_node_id = ANY(@nodeIds)
            ) t GROUP BY node_id
            """;
        var rows = await connection.QueryAsync<DegreeRow>(new CommandDefinition(sql, new { nodeIds }, cancellationToken: cancellationToken));
        return rows.ToDictionary(r => r.NodeId, r => (int)r.Cnt);
    }

    private static async Task<Dictionary<long, IReadOnlyList<Address>>> LoadAddressesAsync(
        NpgsqlConnection connection, long[] internalIds, CancellationToken cancellationToken)
    {
        var ids = internalIds.Where(id => id != 0).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<long, IReadOnlyList<Address>>();

        var rows = await connection.QueryAsync<AddressRow>(new CommandDefinition(
            "SELECT id, internal_id, type_id, address, port FROM node_addresses WHERE internal_id = ANY(@ids) ORDER BY id",
            new { ids }, cancellationToken: cancellationToken));

        return rows
            .Where(r => r.InternalId.HasValue)
            .GroupBy(r => r.InternalId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Address>)g.Select(r => r.ToAddress()).ToList());
    }
}

using Dapper;
using LN_history.Data.Internal;
using LN_History.Model;
using LN_History.Model.Enums;
using Npgsql;

namespace LN_history.Data.DataStores;

public class StatsDataStore : IStatsDataStore
{
    private readonly NpgsqlDataSource _dataSource;

    public StatsDataStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        DapperConfiguration.EnsureConfigured();
    }

    public async Task<IReadOnlyList<ChannelStat>> TopChannelsAsync(ChannelRankBy by, int limit, CancellationToken cancellationToken)
    {
        var sql = by switch
        {
            ChannelRankBy.Capacity => """
                SELECT c.scid, c.capacity_sat, c.funding_timestamp, c.closing_timestamp,
                       COALESCE(cuc.count_direction_0 + cuc.count_direction_1, 0) AS update_count
                FROM (SELECT scid, capacity_sat, funding_timestamp, closing_timestamp
                      FROM channels ORDER BY capacity_sat DESC NULLS LAST LIMIT @limit) c
                LEFT JOIN channel_update_counts cuc ON cuc.scid = c.scid
                ORDER BY c.capacity_sat DESC NULLS LAST
                """,
            ChannelRankBy.UpdateCount => """
                SELECT c.scid, c.capacity_sat, c.funding_timestamp, c.closing_timestamp, t.update_count
                FROM (SELECT scid, (count_direction_0 + count_direction_1) AS update_count
                      FROM channel_update_counts ORDER BY update_count DESC LIMIT @limit) t
                JOIN channels c ON c.scid = t.scid
                ORDER BY t.update_count DESC
                """,
            ChannelRankBy.Lifetime => """
                SELECT c.scid, c.capacity_sat, c.funding_timestamp, c.closing_timestamp,
                       COALESCE(cuc.count_direction_0 + cuc.count_direction_1, 0) AS update_count
                FROM (SELECT scid, capacity_sat, funding_timestamp, closing_timestamp
                      FROM channels ORDER BY (COALESCE(closing_timestamp, now()) - funding_timestamp) DESC LIMIT @limit) c
                LEFT JOIN channel_update_counts cuc ON cuc.scid = c.scid
                ORDER BY (COALESCE(c.closing_timestamp, now()) - c.funding_timestamp) DESC
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(by))
        };

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<ChannelStat>(new CommandDefinition(sql, new { limit }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<NodeStat>> TopNodesAsync(NodeRankBy by, int limit, CancellationToken cancellationToken)
    {
        var sql = by switch
        {
            NodeRankBy.Channels => """
                SELECT t.node_id, t.channel_count, COALESCE(n.announcement_count, 0) AS announcement_count, 0::bigint AS total_capacity_sat
                FROM (SELECT node_id, count(*)::int AS channel_count FROM (
                          SELECT source_node_id AS node_id FROM channels WHERE closing_timestamp IS NULL
                          UNION ALL SELECT target_node_id FROM channels WHERE closing_timestamp IS NULL
                      ) s GROUP BY node_id ORDER BY channel_count DESC LIMIT @limit) t
                LEFT JOIN nodes n ON n.node_id = t.node_id
                ORDER BY t.channel_count DESC
                """,
            NodeRankBy.Announcements => """
                SELECT n.node_id, 0 AS channel_count, n.announcement_count, 0::bigint AS total_capacity_sat
                FROM nodes n ORDER BY n.announcement_count DESC LIMIT @limit
                """,
            NodeRankBy.Capacity => """
                SELECT t.node_id, 0 AS channel_count, COALESCE(n.announcement_count, 0) AS announcement_count, t.total_capacity_sat
                FROM (SELECT node_id, sum(capacity_sat)::bigint AS total_capacity_sat FROM (
                          SELECT source_node_id AS node_id, capacity_sat FROM channels WHERE closing_timestamp IS NULL
                          UNION ALL SELECT target_node_id AS node_id, capacity_sat FROM channels WHERE closing_timestamp IS NULL
                      ) s GROUP BY node_id ORDER BY total_capacity_sat DESC NULLS LAST LIMIT @limit) t
                LEFT JOIN nodes n ON n.node_id = t.node_id
                ORDER BY t.total_capacity_sat DESC NULLS LAST
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(by))
        };

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<NodeStat>(new CommandDefinition(sql, new { limit }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<NetworkStats> GetNetworkStatsAsync(DateTime? at, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        long nodeCount;
        NetworkChannelAgg channels;

        if (at is null)
        {
            nodeCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT count(*) FROM nodes", cancellationToken: cancellationToken));
            channels = await connection.QuerySingleAsync<NetworkChannelAgg>(new CommandDefinition(
                "SELECT count(*) AS channel_count, COALESCE(sum(capacity_sat), 0) AS total_capacity_sat FROM channels WHERE closing_timestamp IS NULL",
                cancellationToken: cancellationToken));
        }
        else
        {
            nodeCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT count(*) FROM nodes WHERE first_seen <= @at AND last_seen >= @at", new { at }, cancellationToken: cancellationToken));
            channels = await connection.QuerySingleAsync<NetworkChannelAgg>(new CommandDefinition(
                """
                SELECT count(*) AS channel_count, COALESCE(sum(capacity_sat), 0) AS total_capacity_sat
                FROM channels WHERE funding_timestamp <= @at AND (closing_timestamp IS NULL OR closing_timestamp > @at)
                """, new { at }, cancellationToken: cancellationToken));
        }

        return new NetworkStats(at, nodeCount, channels.ChannelCount, channels.TotalCapacitySat);
    }

    public async Task<ClosureStats> GetClosureStatsAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken)
    {
        var clauses = new List<string>();
        if (from is not null) clauses.Add("closing_timestamp >= @from");
        if (to is not null) clauses.Add("closing_timestamp <= @to");
        var where = clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);

        var sql = $"""
            SELECT type::text AS type_text, count(*) AS count, COALESCE(sum(mining_fee_sat), 0) AS total_mining_fee_sat
            FROM channel_closures {where}
            GROUP BY type
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        var rows = (await connection.QueryAsync<ClosureAggRow>(new CommandDefinition(sql, new { from, to }, cancellationToken: cancellationToken))).ToList();

        var byType = rows
            .Select(r => new ClosureTypeCount(ParseClosureType(r.TypeText), r.Count, r.TotalMiningFeeSat))
            .ToList();

        return new ClosureStats(byType.Sum(t => t.Count), byType.Sum(t => t.TotalMiningFeeSat), byType);
    }

    private static ClosureType ParseClosureType(string text) =>
        Enum.TryParse<ClosureType>(text, ignoreCase: true, out var value) ? value : ClosureType.Unknown;

    private sealed class NetworkChannelAgg
    {
        public long ChannelCount { get; set; }
        public long TotalCapacitySat { get; set; }
    }

    private sealed class ClosureAggRow
    {
        public string TypeText { get; set; } = string.Empty;
        public long Count { get; set; }
        public long TotalMiningFeeSat { get; set; }
    }
}

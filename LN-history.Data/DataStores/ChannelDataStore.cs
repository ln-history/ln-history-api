using Dapper;
using LN_history.Data.Internal;
using LN_History.Model;
using LN_History.Model.Enums;
using Npgsql;

namespace LN_history.Data.DataStores;

public class ChannelDataStore : IChannelDataStore
{
    private readonly NpgsqlDataSource _dataSource;

    public ChannelDataStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        DapperConfiguration.EnsureConfigured();
    }

    private const string ChannelColumns =
        "scid, funding_timestamp, closing_timestamp, capacity_sat, source_node_id, target_node_id, gossip_id, internal_id";

    public async Task<Channel?> GetByScidAsync(long scid, DateTime? asOf, bool includeRawGossip, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var rawColumn = includeRawGossip ? "raw_gossip" : "NULL::bytea AS raw_gossip";
        var channelSql = $"SELECT {ChannelColumns}, {rawColumn} FROM channels WHERE scid = @scid";

        var channelRow = await connection.QuerySingleOrDefaultAsync<ChannelRow>(
            new CommandDefinition(channelSql, new { scid }, cancellationToken: cancellationToken));
        if (channelRow is null) return null;

        var channel = channelRow.ToChannel();
        channel.Policies = await LoadPoliciesAsync(connection, scid, asOf ?? DateTime.UtcNow, cancellationToken);
        return channel;
    }

    private static async Task<IReadOnlyDictionary<Direction, DirectionPolicy>> LoadPoliciesAsync(
        NpgsqlConnection connection, long scid, DateTime asOf, CancellationToken cancellationToken)
    {
        // Active update per direction = latest valid_from <= asOf (contiguous chain => that row is the active one).
        const string policySql = """
            SELECT DISTINCT ON (direction)
                   direction::int AS direction_value,
                   COALESCE(message_flags, 0)                AS message_flags,
                   COALESCE(channel_flags, 0)                AS channel_flags,
                   COALESCE(cltv_expiry_delta, 0)            AS cltv_expiry_delta,
                   COALESCE(htlc_minimum_msat, 0)            AS htlc_minimum_msat,
                   COALESCE(fee_base_msat, 0)                AS fee_base_msat,
                   COALESCE(fee_proportional_millionths, 0)  AS fee_proportional_millionths,
                   htlc_maximum_msat,
                   valid_from, valid_to, is_fee_update, is_topology_update, gossip_id, internal_id, scid
            FROM channel_updates
            WHERE scid = @scid AND direction IS NOT NULL AND valid_from <= @asOf
            ORDER BY direction, valid_from DESC
            """;

        var updates = (await connection.QueryAsync<ChannelUpdateRow>(
            new CommandDefinition(policySql, new { scid, asOf }, cancellationToken: cancellationToken))).ToList();

        const string countsSql = "SELECT count_direction_0 AS dir0, count_direction_1 AS dir1 FROM channel_update_counts WHERE scid = @scid";
        var counts = await connection.QuerySingleOrDefaultAsync<ChannelUpdateCountsRow>(
            new CommandDefinition(countsSql, new { scid }, cancellationToken: cancellationToken))
            ?? new ChannelUpdateCountsRow();

        var policies = new Dictionary<Direction, DirectionPolicy>();
        foreach (var direction in new[] { Direction.NodeOneToNodeTwo, Direction.NodeTwoToNodeOne })
        {
            var update = updates.FirstOrDefault(u => u.DirectionValue == (int)direction);
            policies[direction] = new DirectionPolicy
            {
                FeePolicy = update?.ToFeePolicy(),
                TotalUpdateCount = direction == Direction.NodeOneToNodeTwo ? counts.Dir0 : counts.Dir1
            };
        }
        return policies;
    }

    public async Task<Page<Channel>> GetChannelsAsync(ChannelListFilter filter, CancellationToken cancellationToken)
    {
        var (where, parameters) = BuildChannelFilter(filter, nodeId: null);
        return await QueryChannelPageAsync(where, parameters, filter, cancellationToken);
    }

    public async Task<Page<Channel>> GetChannelsByNodeAsync(string nodeId, ChannelListFilter filter, CancellationToken cancellationToken)
    {
        var (where, parameters) = BuildChannelFilter(filter, nodeId);
        return await QueryChannelPageAsync(where, parameters, filter, cancellationToken);
    }

    private async Task<Page<Channel>> QueryChannelPageAsync(
        string where, DynamicParameters parameters, ChannelListFilter filter, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var total = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition($"SELECT count(*) FROM channels {where}", parameters, cancellationToken: cancellationToken));
        if (total == 0) return Page<Channel>.Empty(filter.Limit, filter.Offset);

        var rawColumn = filter.IncludeRawGossip ? "raw_gossip" : "NULL::bytea AS raw_gossip";
        parameters.Add("limit", filter.Limit);
        parameters.Add("offset", filter.Offset);

        var sql = $"SELECT {ChannelColumns}, {rawColumn} FROM channels {where} ORDER BY scid LIMIT @limit OFFSET @offset";
        var rows = await connection.QueryAsync<ChannelRow>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = rows.Select(r => r.ToChannel()).ToList();
        return new Page<Channel>(items, total, filter.Limit, filter.Offset);
    }

    private static (string Where, DynamicParameters Parameters) BuildChannelFilter(ChannelListFilter filter, string? nodeId)
    {
        var parameters = new DynamicParameters();
        var clauses = new List<string>();

        if (nodeId is not null)
        {
            clauses.Add("(source_node_id = @nodeId OR target_node_id = @nodeId)");
            parameters.Add("nodeId", nodeId);
        }

        if (filter.OpenAt is { } openAt)
        {
            clauses.Add("funding_timestamp <= @openAt AND (closing_timestamp IS NULL OR closing_timestamp > @openAt)");
            parameters.Add("openAt", openAt);
        }
        else if (filter.CurrentlyOpen)
        {
            clauses.Add("closing_timestamp IS NULL");
        }

        var where = clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
        return (where, parameters);
    }

    public async Task<IReadOnlyList<ChannelUpdate>> GetUpdateHistoryAsync(long scid, DateTime? until, bool includeRawGossip, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var rawColumn = includeRawGossip ? "cu.raw_gossip" : "NULL::bytea AS raw_gossip";
        var untilClause = until is null ? string.Empty : "AND cu.valid_from <= @until";

        var sql = $"""
            SELECT cu.scid,
                   cu.direction::int AS direction_value,
                   cu.valid_from, cu.valid_to,
                   COALESCE(cu.message_flags, 0)               AS message_flags,
                   COALESCE(cu.channel_flags, 0)               AS channel_flags,
                   COALESCE(cu.cltv_expiry_delta, 0)           AS cltv_expiry_delta,
                   COALESCE(cu.htlc_minimum_msat, 0)           AS htlc_minimum_msat,
                   COALESCE(cu.fee_base_msat, 0)               AS fee_base_msat,
                   COALESCE(cu.fee_proportional_millionths, 0) AS fee_proportional_millionths,
                   cu.htlc_maximum_msat, cu.is_fee_update, cu.is_topology_update, cu.gossip_id, cu.internal_id,
                   {rawColumn},
                   CASE WHEN cu.direction = B'0' THEN c.source_node_id ELSE c.target_node_id END AS source_node_id,
                   CASE WHEN cu.direction = B'0' THEN c.target_node_id ELSE c.source_node_id END AS target_node_id
            FROM channel_updates cu
            JOIN channels c ON c.scid = cu.scid
            WHERE cu.scid = @scid AND cu.direction IS NOT NULL {untilClause}
            ORDER BY cu.valid_from ASC
            """;

        var rows = await connection.QueryAsync<ChannelUpdateRow>(
            new CommandDefinition(sql, new { scid, until }, cancellationToken: cancellationToken));
        return rows.Select(r => r.ToChannelUpdate()).ToList();
    }

    public async Task<byte[]> GetHistoryRawAsync(long scid, DateTime? until, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var untilClause = until is null ? string.Empty : "AND valid_from <= @until";
        var sql = $"""
            SELECT raw_gossip FROM (
                SELECT raw_gossip, 0 AS ord, funding_timestamp AS ts FROM channels
                WHERE scid = @scid AND raw_gossip IS NOT NULL
                UNION ALL
                SELECT raw_gossip, 1 AS ord, valid_from AS ts FROM channel_updates
                WHERE scid = @scid AND raw_gossip IS NOT NULL {untilClause}
            ) parts
            ORDER BY ord, ts
            """;

        var blobs = await connection.QueryAsync<byte[]>(
            new CommandDefinition(sql, new { scid, until }, cancellationToken: cancellationToken));
        return GossipBytes.Concat(blobs);
    }

    public async Task<bool> ExistsAsync(long scid, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition("SELECT EXISTS(SELECT 1 FROM channels WHERE scid = @scid)", new { scid }, cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ChannelCapacity>> GetCapacitiesAsync(DateTime? openAt, bool currentlyOpen, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        var clauses = new List<string> { "scid IS NOT NULL" };
        var parameters = new DynamicParameters();

        if (openAt is { } at)
        {
            clauses.Add("funding_timestamp <= @openAt AND (closing_timestamp IS NULL OR closing_timestamp > @openAt)");
            parameters.Add("openAt", at);
        }
        else if (currentlyOpen)
        {
            clauses.Add("closing_timestamp IS NULL");
        }

        var sql = $"SELECT scid, COALESCE(capacity_sat, 0) AS capacity_sat FROM channels WHERE {string.Join(" AND ", clauses)}";
        var rows = await connection.QueryAsync<ChannelCapacity>(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        return rows.ToList();
    }
}

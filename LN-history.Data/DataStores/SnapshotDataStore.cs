using Dapper;
using LN_history.Data.Internal;
using LN_History.Model;
using Npgsql;

namespace LN_history.Data.DataStores;

public class SnapshotDataStore : ISnapshotDataStore
{
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>Guard for the deliberately-heavy snapshot/diff queries (milliseconds).</summary>
    private const int StatementTimeoutMs = 60_000;

    public SnapshotDataStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        DapperConfiguration.EnsureConfigured();
    }

    public async Task<byte[]> GetSnapshotRawAsync(DateTime timestamp, bool withUpdates, CancellationToken cancellationToken)
    {
        var updatesUnion = withUpdates
            ? """
              UNION ALL
              SELECT cu.raw_gossip
              FROM open_channels oc
              CROSS JOIN (VALUES (B'0'), (B'1')) AS d(direction)
              JOIN LATERAL (
                  SELECT raw_gossip FROM channel_updates c
                  WHERE c.scid = oc.scid AND c.direction = d.direction AND c.valid_from <= @timestamp
                  ORDER BY c.valid_from DESC LIMIT 1
              ) cu ON true
              WHERE cu.raw_gossip IS NOT NULL
              """
            : string.Empty;

        var sql = $"""
            WITH open_channels AS (
                SELECT scid, raw_gossip FROM channels
                WHERE funding_timestamp <= @timestamp AND (closing_timestamp IS NULL OR closing_timestamp > @timestamp)
            )
            SELECT raw_gossip FROM open_channels WHERE raw_gossip IS NOT NULL
            UNION ALL
            SELECT raw_gossip FROM node_announcements
            WHERE valid_from <= @timestamp AND (valid_to > @timestamp OR valid_to IS NULL) AND raw_gossip IS NOT NULL
            {updatesUnion}
            """;

        return await ConcatWithTimeoutAsync(sql, new { timestamp }, cancellationToken);
    }

    public async Task<byte[]> GetDiffRawAsync(DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT raw_gossip FROM (
                SELECT raw_gossip, funding_timestamp AS ts FROM channels
                WHERE funding_timestamp BETWEEN @start AND @end AND raw_gossip IS NOT NULL
                UNION ALL
                SELECT raw_gossip, valid_from AS ts FROM channel_updates
                WHERE valid_from BETWEEN @start AND @end AND raw_gossip IS NOT NULL
                UNION ALL
                SELECT raw_gossip, valid_from AS ts FROM node_announcements_complete
                WHERE valid_from BETWEEN @start AND @end AND raw_gossip IS NOT NULL
            ) e
            ORDER BY ts
            """;

        return await ConcatWithTimeoutAsync(sql, new { start, end }, cancellationToken);
    }

    public async Task<IReadOnlyList<GossipEvent>> GetDiffEventsAsync(DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTimeoutAsync(connection, cancellationToken);

        var channelRows = await connection.QueryAsync<ChannelRow>(new CommandDefinition(
            $"SELECT scid, funding_timestamp, closing_timestamp, capacity_sat, source_node_id, target_node_id, gossip_id, internal_id, NULL::bytea AS raw_gossip " +
            "FROM channels WHERE funding_timestamp BETWEEN @start AND @end",
            new { start, end }, transaction, cancellationToken: cancellationToken));

        var updateRows = await connection.QueryAsync<ChannelUpdateRow>(new CommandDefinition(
            $"""
             SELECT cu.scid, cu.direction::int AS direction_value, cu.valid_from, cu.valid_to,
                    COALESCE(cu.message_flags,0) AS message_flags, COALESCE(cu.channel_flags,0) AS channel_flags,
                    COALESCE(cu.cltv_expiry_delta,0) AS cltv_expiry_delta, COALESCE(cu.htlc_minimum_msat,0) AS htlc_minimum_msat,
                    COALESCE(cu.fee_base_msat,0) AS fee_base_msat, COALESCE(cu.fee_proportional_millionths,0) AS fee_proportional_millionths,
                    cu.htlc_maximum_msat, cu.is_fee_update, cu.is_topology_update, cu.gossip_id, cu.internal_id, NULL::bytea AS raw_gossip,
                    CASE WHEN cu.direction = B'0' THEN c.source_node_id ELSE c.target_node_id END AS source_node_id,
                    CASE WHEN cu.direction = B'0' THEN c.target_node_id ELSE c.source_node_id END AS target_node_id
             FROM channel_updates cu JOIN channels c ON c.scid = cu.scid
             WHERE cu.valid_from BETWEEN @start AND @end AND cu.direction IS NOT NULL
             """,
            new { start, end }, transaction, cancellationToken: cancellationToken));

        var announcementRows = (await connection.QueryAsync<NodeAnnouncementRow>(new CommandDefinition(
            "SELECT gossip_id, node_id, alias, rgb_color, features, valid_from, is_data_update, internal_id, NULL::bytea AS raw_gossip " +
            "FROM node_announcements_complete WHERE valid_from BETWEEN @start AND @end",
            new { start, end }, transaction, cancellationToken: cancellationToken))).ToList();

        var addresses = await LoadAddressesAsync(connection, transaction, announcementRows.Select(r => r.InternalId).ToArray(), cancellationToken);

        var events = new List<GossipEvent>();
        events.AddRange(channelRows.Select(r => GossipEvent.ForChannel(r.ToChannel())));
        events.AddRange(updateRows.Select(r => GossipEvent.ForChannelUpdate(r.ToChannelUpdate())));
        events.AddRange(announcementRows.Select(r => GossipEvent.ForNode(r.ToNodeAnnouncement(addresses.GetValueOrDefault(r.InternalId)))));

        return events.OrderBy(e => e.Timestamp).ToList();
    }

    private async Task<byte[]> ConcatWithTimeoutAsync(string sql, object parameters, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SetTimeoutAsync(connection, cancellationToken);

        var blobs = await connection.QueryAsync<byte[]>(new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));
        return GossipBytes.Concat(blobs);
    }

    private static Task SetTimeoutAsync(NpgsqlConnection connection, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition($"SET LOCAL statement_timeout = {StatementTimeoutMs}", cancellationToken: cancellationToken));

    private static async Task<Dictionary<long, IReadOnlyList<Address>>> LoadAddressesAsync(
        NpgsqlConnection connection, System.Data.Common.DbTransaction transaction, long[] internalIds, CancellationToken cancellationToken)
    {
        var ids = internalIds.Where(id => id != 0).Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<long, IReadOnlyList<Address>>();

        var rows = await connection.QueryAsync<AddressRow>(new CommandDefinition(
            "SELECT id, internal_id, type_id, address, port FROM node_addresses WHERE internal_id = ANY(@ids) ORDER BY id",
            new { ids }, transaction, cancellationToken: cancellationToken));

        return rows
            .Where(r => r.InternalId.HasValue)
            .GroupBy(r => r.InternalId!.Value)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Address>)g.Select(r => r.ToAddress()).ToList());
    }
}

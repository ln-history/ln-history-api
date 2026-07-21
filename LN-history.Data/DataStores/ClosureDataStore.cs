using Dapper;
using LN_history.Data.Internal;
using LN_History.Model;
using Npgsql;

namespace LN_history.Data.DataStores;

public class ClosureDataStore : IClosureDataStore
{
    private readonly NpgsqlDataSource _dataSource;

    public ClosureDataStore(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
        DapperConfiguration.EnsureConfigured();
    }

    public async Task<ChannelClosure?> GetByScidAsync(long scid, CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        const string sql = """
            SELECT c.scid                 AS scid,
                   cl.type::text          AS type_text,
                   cl.mining_fee_sat,
                   cl.closing_txid,
                   cl.closing_height,
                   cl.closing_timestamp,
                   cl.settled_balance_sat,
                   cl.output_0_sat,
                   cl.output_1_sat,
                   cl.balance_node_1_sat,
                   cl.balance_node_2_sat
            FROM channel_closures cl
            JOIN channels c ON c.gossip_id = cl.gossip_id
            WHERE c.scid = @scid
            """;

        var row = await connection.QuerySingleOrDefaultAsync<ClosureRow>(
            new CommandDefinition(sql, new { scid }, cancellationToken: cancellationToken));
        return row?.ToChannelClosure();
    }
}

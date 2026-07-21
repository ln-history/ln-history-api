using LN_History.Model;
using LN_History.Model.Enums;

namespace LN_history.Data.DataStores;

public interface IStatsDataStore
{
    Task<IReadOnlyList<ChannelStat>> TopChannelsAsync(ChannelRankBy by, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<NodeStat>> TopNodesAsync(NodeRankBy by, int limit, CancellationToken cancellationToken);

    /// <summary>Network-wide counts currently (at = null) or at a point in time.</summary>
    Task<NetworkStats> GetNetworkStatsAsync(DateTime? at, CancellationToken cancellationToken);

    /// <summary>Closure counts and mining-fee totals, grouped by type, over an optional [from, to] window.</summary>
    Task<ClosureStats> GetClosureStatsAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken);
}

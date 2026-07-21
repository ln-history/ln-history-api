using LN_History.Model;
using LN_History.Model.Enums;

namespace LN_history.Data.DataStores;

public interface IStatsDataStore
{
    Task<IReadOnlyList<ChannelStat>> TopChannelsAsync(ChannelRankBy by, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<NodeStat>> TopNodesAsync(NodeRankBy by, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Network-wide counts. At a point in time when <paramref name="at"/> is set; otherwise current —
    /// <paramref name="currentlyActive"/> counts only nodes seen in the last 14 days, else all nodes.
    /// </summary>
    Task<NetworkStats> GetNetworkStatsAsync(DateTime? at, bool currentlyActive, CancellationToken cancellationToken);

    /// <summary>Closure counts and mining-fee totals, grouped by type, over an optional [from, to] window.</summary>
    Task<ClosureStats> GetClosureStatsAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken);
}

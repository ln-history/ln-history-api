using LN_History.Model;
using LN_History.Model.Enums;

namespace LN_history.Core.Services;

public interface IStatsService
{
    Task<IReadOnlyList<ChannelStat>> TopChannelsAsync(ChannelRankBy by, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<NodeStat>> TopNodesAsync(NodeRankBy by, int limit, CancellationToken cancellationToken);

    Task<NetworkStats> GetNetworkStatsAsync(DateTime? at, bool currentlyActive, CancellationToken cancellationToken);

    Task<ClosureStats> GetClosureStatsAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken);
}

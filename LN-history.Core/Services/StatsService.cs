using LN_history.Data.DataStores;
using LN_History.Model;
using LN_History.Model.Enums;

namespace LN_history.Core.Services;

public class StatsService : IStatsService
{
    private readonly IStatsDataStore _stats;

    public StatsService(IStatsDataStore stats)
    {
        _stats = stats;
    }

    public Task<IReadOnlyList<ChannelStat>> TopChannelsAsync(ChannelRankBy by, int limit, CancellationToken cancellationToken) =>
        _stats.TopChannelsAsync(by, limit, cancellationToken);

    public Task<IReadOnlyList<NodeStat>> TopNodesAsync(NodeRankBy by, int limit, CancellationToken cancellationToken) =>
        _stats.TopNodesAsync(by, limit, cancellationToken);

    public Task<NetworkStats> GetNetworkStatsAsync(DateTime? at, bool currentlyActive, CancellationToken cancellationToken) =>
        _stats.GetNetworkStatsAsync(at, currentlyActive, cancellationToken);

    public Task<ClosureStats> GetClosureStatsAsync(DateTime? from, DateTime? to, CancellationToken cancellationToken) =>
        _stats.GetClosureStatsAsync(from, to, cancellationToken);
}

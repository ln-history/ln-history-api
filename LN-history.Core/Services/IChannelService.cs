using LN_History.Model;

namespace LN_history.Core.Services;

public interface IChannelService
{
    /// <summary>
    /// Single channel by scid, with per-direction policies. Optionally expands node_1/node_2 and
    /// includes raw gossip (and the raw closing transaction from the Bitcoin node when closed).
    /// </summary>
    Task<Channel?> GetChannelAsync(long scid, DateTime? asOf, bool includeNodes, bool includeRawGossip, CancellationToken cancellationToken);

    Task<Page<Channel>> GetChannelsAsync(ChannelListFilter filter, CancellationToken cancellationToken);

    Task<Page<Channel>> GetChannelsByNodeAsync(string nodeId, ChannelListFilter filter, CancellationToken cancellationToken);

    Task<IReadOnlyList<ChannelUpdate>> GetUpdateHistoryAsync(long scid, DateTime? until, bool includeRawGossip, CancellationToken cancellationToken);

    Task<byte[]> GetChannelHistoryRawAsync(long scid, DateTime? until, CancellationToken cancellationToken);

    /// <summary>scid + capacity_sat for all channels matching the open/at-T/all selection (for graph construction).</summary>
    Task<IReadOnlyList<ChannelCapacity>> GetCapacitiesAsync(DateTime? openAt, bool currentlyOpen, CancellationToken cancellationToken);
}

using LN_History.Model;

namespace LN_history.Data.DataStores;

public interface IChannelDataStore
{
    /// <summary>Single channel by scid, with per-direction policies populated as of <paramref name="asOf"/> (null = now).</summary>
    Task<Channel?> GetByScidAsync(long scid, DateTime? asOf, bool includeRawGossip, CancellationToken cancellationToken);

    /// <summary>Paged list of channels (no per-direction policies).</summary>
    Task<Page<Channel>> GetChannelsAsync(ChannelListFilter filter, CancellationToken cancellationToken);

    /// <summary>Paged list of channels a node participates in (as source or target).</summary>
    Task<Page<Channel>> GetChannelsByNodeAsync(string nodeId, ChannelListFilter filter, CancellationToken cancellationToken);

    /// <summary>Full channel_update chain for a channel, ordered by valid_from, optionally up to <paramref name="until"/>.</summary>
    Task<IReadOnlyList<ChannelUpdate>> GetUpdateHistoryAsync(long scid, DateTime? until, bool includeRawGossip, CancellationToken cancellationToken);

    /// <summary>Concatenated raw gossip for a channel (announcement + all updates up to <paramref name="until"/>).</summary>
    Task<byte[]> GetHistoryRawAsync(long scid, DateTime? until, CancellationToken cancellationToken);

    /// <summary>True if a channel with this scid exists.</summary>
    Task<bool> ExistsAsync(long scid, CancellationToken cancellationToken);
}

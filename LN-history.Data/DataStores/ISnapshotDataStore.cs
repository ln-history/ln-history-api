using LN_History.Model;

namespace LN_history.Data.DataStores;

public interface ISnapshotDataStore
{
    /// <summary>
    /// Concatenated raw gossip valid at <paramref name="timestamp"/>: open channel_announcements +
    /// active node_announcements, plus active channel_updates when <paramref name="withUpdates"/> is true.
    /// </summary>
    Task<byte[]> GetSnapshotRawAsync(DateTime timestamp, bool withUpdates, CancellationToken cancellationToken);

    /// <summary>Concatenated raw gossip for everything that first appeared in [start, end], ordered by time.</summary>
    Task<byte[]> GetDiffRawAsync(DateTime start, DateTime end, CancellationToken cancellationToken);

    /// <summary>Chronologically-ordered parsed events (channels, channel_updates, node announcements) in [start, end].</summary>
    Task<IReadOnlyList<GossipEvent>> GetDiffEventsAsync(DateTime start, DateTime end, CancellationToken cancellationToken);
}

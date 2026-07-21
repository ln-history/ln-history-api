using LN_History.Model;

namespace LN_history.Data.DataStores;

public interface INodeDataStore
{
    /// <summary>
    /// Single node by pubkey with current active announcement (as of <paramref name="asOf"/>, null = now)
    /// and open-channel degree. All-time degree is populated only when <paramref name="includeAllTimeDegree"/> is true.
    /// </summary>
    Task<Node?> GetByIdAsync(string nodeId, DateTime? asOf, bool includeAllTimeDegree, bool includeRawGossip, CancellationToken cancellationToken);

    /// <summary>Paged list of nodes that existed at <paramref name="existedAt"/> (null = all nodes), with open degree and announcement count.</summary>
    Task<Page<Node>> GetNodesAsync(DateTime? existedAt, int limit, int offset, CancellationToken cancellationToken);

    /// <summary>Full node_announcement chain for a node, ordered by valid_from, with addresses.</summary>
    Task<IReadOnlyList<NodeAnnouncement>> GetAnnouncementHistoryAsync(string nodeId, DateTime? until, bool includeRawGossip, CancellationToken cancellationToken);

    /// <summary>Concatenated raw gossip for all of a node's announcements up to <paramref name="until"/>.</summary>
    Task<byte[]> GetHistoryRawAsync(string nodeId, DateTime? until, CancellationToken cancellationToken);

    /// <summary>True if a node with this pubkey exists.</summary>
    Task<bool> ExistsAsync(string nodeId, CancellationToken cancellationToken);
}

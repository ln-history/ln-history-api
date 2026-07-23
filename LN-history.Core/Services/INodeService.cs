using LN_History.Model;

namespace LN_history.Core.Services;

public interface INodeService
{
    /// <summary>Single node with current announcement and open degree (all-time degree optional).</summary>
    Task<Node?> GetNodeAsync(string nodeId, DateTime? asOf, bool includeAllTimeDegree, bool includeRawGossip, CancellationToken cancellationToken);

    Task<Page<Node>> GetNodesAsync(DateTime? existedAt, bool currentlyActive, int limit, int offset, CancellationToken cancellationToken);

    /// <summary>Node with its full announcement chain (up to <paramref name="until"/>), for the history endpoint.</summary>
    Task<Node?> GetNodeHistoryAsync(string nodeId, DateTime? until, bool includeRawGossip, CancellationToken cancellationToken);

    Task<byte[]> GetNodeHistoryRawAsync(string nodeId, DateTime? until, CancellationToken cancellationToken);
}

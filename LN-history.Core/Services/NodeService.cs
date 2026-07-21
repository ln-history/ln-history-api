using LN_history.Data.DataStores;
using LN_History.Model;

namespace LN_history.Core.Services;

public class NodeService : INodeService
{
    private readonly INodeDataStore _nodes;

    public NodeService(INodeDataStore nodes)
    {
        _nodes = nodes;
    }

    public Task<Node?> GetNodeAsync(string nodeId, DateTime? asOf, bool includeAllTimeDegree, bool includeRawGossip, CancellationToken cancellationToken) =>
        _nodes.GetByIdAsync(nodeId, asOf, includeAllTimeDegree, includeRawGossip, cancellationToken);

    public Task<Page<Node>> GetNodesAsync(DateTime? existedAt, bool currentlyActive, int limit, int offset, CancellationToken cancellationToken) =>
        _nodes.GetNodesAsync(existedAt, currentlyActive, limit, offset, cancellationToken);

    public async Task<Node?> GetNodeHistoryAsync(string nodeId, DateTime? until, bool includeRawGossip, CancellationToken cancellationToken)
    {
        var node = await _nodes.GetByIdAsync(nodeId, asOf: null, includeAllTimeDegree: false, includeRawGossip: false, cancellationToken);
        if (node is null) return null;

        node.Announcements = await _nodes.GetAnnouncementHistoryAsync(nodeId, until, includeRawGossip, cancellationToken);
        return node;
    }

    public Task<byte[]> GetNodeHistoryRawAsync(string nodeId, DateTime? until, CancellationToken cancellationToken) =>
        _nodes.GetHistoryRawAsync(nodeId, until, cancellationToken);
}

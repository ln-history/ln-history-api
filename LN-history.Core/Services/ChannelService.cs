using Bitcoin.Data.DataStores;
using LN_history.Data.DataStores;
using LN_History.Model;

namespace LN_history.Core.Services;

public class ChannelService : IChannelService
{
    private readonly IChannelDataStore _channels;
    private readonly IClosureDataStore _closures;
    private readonly INodeDataStore _nodes;
    private readonly IBlockDataStore _blocks;

    public ChannelService(IChannelDataStore channels, IClosureDataStore closures, INodeDataStore nodes, IBlockDataStore blocks)
    {
        _channels = channels;
        _closures = closures;
        _nodes = nodes;
        _blocks = blocks;
    }

    public async Task<Channel?> GetChannelAsync(long scid, DateTime? asOf, bool includeNodes, bool includeRawGossip, CancellationToken cancellationToken)
    {
        var channel = await _channels.GetByScidAsync(scid, asOf, includeRawGossip, cancellationToken);
        if (channel is null) return null;

        if (channel.ClosingTimestamp is not null)
        {
            channel.Closure = await _closures.GetByScidAsync(scid, cancellationToken);
            if (includeRawGossip && channel.Closure is not null)
            {
                channel.Closure.RawTransaction = await _blocks.GetRawTransactionAsync(channel.Closure.ClosingTxid, cancellationToken);
            }
        }

        if (includeNodes)
        {
            channel.Node1 = await _nodes.GetByIdAsync(channel.NodeId1, asOf, includeAllTimeDegree: false, includeRawGossip: false, cancellationToken);
            channel.Node2 = await _nodes.GetByIdAsync(channel.NodeId2, asOf, includeAllTimeDegree: false, includeRawGossip: false, cancellationToken);
        }

        return channel;
    }

    public Task<Page<Channel>> GetChannelsAsync(ChannelListFilter filter, CancellationToken cancellationToken) =>
        _channels.GetChannelsAsync(filter, cancellationToken);

    public Task<Page<Channel>> GetChannelsByNodeAsync(string nodeId, ChannelListFilter filter, CancellationToken cancellationToken) =>
        _channels.GetChannelsByNodeAsync(nodeId, filter, cancellationToken);

    public Task<IReadOnlyList<ChannelUpdate>> GetUpdateHistoryAsync(long scid, DateTime? until, bool includeRawGossip, CancellationToken cancellationToken) =>
        _channels.GetUpdateHistoryAsync(scid, until, includeRawGossip, cancellationToken);

    public Task<byte[]> GetChannelHistoryRawAsync(long scid, DateTime? until, CancellationToken cancellationToken) =>
        _channels.GetHistoryRawAsync(scid, until, cancellationToken);

    public Task<IReadOnlyList<ChannelCapacity>> GetCapacitiesAsync(DateTime? openAt, bool currentlyOpen, CancellationToken cancellationToken) =>
        _channels.GetCapacitiesAsync(openAt, currentlyOpen, cancellationToken);
}

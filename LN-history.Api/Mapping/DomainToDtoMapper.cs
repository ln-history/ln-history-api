using LN_history.Api.Dto;
using LN_History.Model;
using LN_History.Model.Enums;

namespace LN_history.Api.Mapping;

public static class DomainToDtoMapper
{
    private static readonly IReadOnlyDictionary<NetworkAddressType, (string Name, string Description)> AddressTypes =
        new Dictionary<NetworkAddressType, (string, string)>
        {
            [NetworkAddressType.IPv4] = ("IPv4", "Standard IPv4 address"),
            [NetworkAddressType.IPv6] = ("IPv6", "Standard IPv6 address"),
            [NetworkAddressType.TorV2] = ("TorV2", "Deprecated Tor v2 onion service"),
            [NetworkAddressType.TorV3] = ("TorV3", "Tor v3 onion service"),
            [NetworkAddressType.Dns] = ("DNS", "DNS hostname"),
        };

    private static string ScidStr(long scid) => new ShortChannelId(scid).ToString();

    /// <summary>Renders a small flags bitfield as an 8-bit (minimum) binary string, e.g. 1 -> "00000001".</summary>
    private static string ToBinary(int flags) => Convert.ToString(flags & 0xFFFF, 2).PadLeft(8, '0');

    public static FeePolicyDto ToDto(this FeePolicy p) => new()
    {
        CltvExpiryDelta = p.CltvExpiryDelta,
        ChannelFlags = ToBinary(p.ChannelFlags),
        FeeBaseMsat = p.FeeBaseMsat,
        FeeProportionalMillionths = p.FeeProportionalMillionths,
        HtlcMinimumMsat = p.HtlcMinimumMsat,
        HtlcMaximumMsat = p.HtlcMaximumMsat
    };

    public static ChannelUpdateDto ToDto(this ChannelUpdate u) => new()
    {
        Scid = u.Scid,
        ScidStr = ScidStr(u.Scid),
        Direction = u.Direction == Direction.NodeTwoToNodeOne,
        SourceNodeId = u.SourceNodeId,
        TargetNodeId = u.TargetNodeId,
        ValidFrom = u.ValidFrom,
        ValidTo = u.ValidTo,
        FeePolicy = u.FeePolicy.ToDto(),
        Timestamp = u.ValidFrom,
        MessageFlags = ToBinary(u.MessageFlags),
        IsTopologyUpdate = u.IsTopologyUpdate,
        IsFeeUpdate = u.IsFeeUpdate,
        GossipId = u.GossipId,
        InternalId = u.InternalId,
        RawGossip = u.RawGossip
    };

    public static ChannelClosureDto ToDto(this ChannelClosure c) => new()
    {
        Scid = c.Scid,
        ScidStr = ScidStr(c.Scid),
        ClosureType = c.Type.ToString().ToLowerInvariant(),
        MiningFee = c.MiningFeeSat,
        Txid = c.ClosingTxid,
        Tx = c.RawTransaction
    };

    public static ChannelDto ToDto(this Channel c) => new()
    {
        Scid = c.Scid,
        ScidStr = ScidStr(c.Scid),
        FundingTimestamp = c.FundingTimestamp,
        ClosingTimestamp = c.ClosingTimestamp,
        ClosingInformation = c.Closure?.ToDto(),
        CapacitySat = c.CapacitySat,
        NodeId1 = c.NodeId1,
        NodeId2 = c.NodeId2,
        Node1 = c.Node1?.ToDto(),
        Node2 = c.Node2?.ToDto(),
        FeePolicies = c.Policies?.ToDictionary(
            kvp => ((int)kvp.Key).ToString(),
            kvp => new DirectionPolicyDto
            {
                FeePolicy = kvp.Value.FeePolicy?.ToDto(),
                TotalUpdateCount = kvp.Value.TotalUpdateCount
            }),
        GossipId = c.GossipId,
        InternalId = c.InternalId,
        RawGossip = c.RawGossip
    };

    public static AddressDto ToDto(this Address a)
    {
        var info = AddressTypes.GetValueOrDefault(a.Type, (a.Type.ToString(), string.Empty));
        return new AddressDto
        {
            Id = a.Id,
            Network = new AddressTypeDto { Id = (int)a.Type, Name = info.Item1, Description = info.Item2 },
            Address = a.Value,
            Port = a.Port
        };
    }

    public static NodeAnnouncementDto ToDto(this NodeAnnouncement a) => new()
    {
        NodeId = a.NodeId,
        Alias = a.Alias,
        RgbColor = a.RgbColor,
        Features = a.Features,
        Addresses = a.Addresses.Select(ToDto).ToList(),
        Timestamp = a.Timestamp,
        IsDataUpdate = a.IsDataUpdate,
        GossipId = a.GossipId,
        InternalId = a.InternalId,
        RawGossip = a.RawGossip
    };

    public static NodeDto ToDto(this Node n) => new()
    {
        NodeId = n.NodeId,
        FirstSeen = n.FirstSeen,
        LastSeen = n.LastSeen,
        NumberOfChannels = n.NumberOfChannels,
        NumberOfChannelsAllTime = n.NumberOfChannelsAllTime,
        NumberOfAnnouncements = n.NumberOfAnnouncements,
        Announcements = n.Announcements.Select(ToDto).ToList()
    };

    public static BlockDto ToDto(this Block b) => new()
    {
        BlockHash = b.BlockHash,
        BlockHeight = b.BlockHeight,
        Timestamp = b.Timestamp,
        SpaceBytes = b.SpaceBytes,
        SubsidySat = b.SubsidySat,
        TxFees = b.TxFees,
        TxCount = b.TxCount
    };

    public static GossipEventDto ToDto(this GossipEvent e) => new()
    {
        EventType = e.EventType,
        Timestamp = e.Timestamp,
        Data = e.EventType switch
        {
            GossipEventType.Channel => ((Channel)e.Data).ToDto(),
            GossipEventType.ChannelUpdate => ((ChannelUpdate)e.Data).ToDto(),
            GossipEventType.Node => ((NodeAnnouncement)e.Data).ToDto(),
            _ => e.Data
        }
    };

    public static ChannelStatDto ToDto(this ChannelStat s) => new()
    {
        Scid = s.Scid,
        ScidStr = ScidStr(s.Scid),
        CapacitySat = s.CapacitySat,
        FundingTimestamp = s.FundingTimestamp,
        ClosingTimestamp = s.ClosingTimestamp,
        UpdateCount = s.UpdateCount
    };

    public static NodeStatDto ToDto(this NodeStat s) => new()
    {
        NodeId = s.NodeId,
        ChannelCount = s.ChannelCount,
        AnnouncementCount = s.AnnouncementCount,
        TotalCapacitySat = s.TotalCapacitySat
    };

    public static NetworkStatsDto ToDto(this NetworkStats s) => new()
    {
        At = s.At,
        NodeCount = s.NodeCount,
        ChannelCount = s.ChannelCount,
        TotalCapacitySat = s.TotalCapacitySat
    };

    public static ClosureStatsDto ToDto(this ClosureStats s) => new()
    {
        Total = s.Total,
        TotalMiningFeeSat = s.TotalMiningFeeSat,
        ByType = s.ByType.Select(t => new ClosureTypeCountDto
        {
            ClosureType = t.Type.ToString().ToLowerInvariant(),
            Count = t.Count,
            TotalMiningFeeSat = t.TotalMiningFeeSat
        }).ToList()
    };

    public static PagedResultDto<TDto> ToDto<TSource, TDto>(this Page<TSource> page, Func<TSource, TDto> map) => new()
    {
        Items = page.Items.Select(map).ToList(),
        Total = page.Total,
        Limit = page.Limit,
        Offset = page.Offset
    };
}

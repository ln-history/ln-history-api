using LN_History.Model;
using LN_History.Model.Enums;

namespace LN_history.Data.Internal;

internal static class RowMappers
{
    public static string? ToHex(byte[]? bytes) =>
        bytes is null ? null : Convert.ToHexString(bytes).ToLowerInvariant();

    public static Channel ToChannel(this ChannelRow row) => new()
    {
        Scid = row.Scid,
        FundingTimestamp = row.FundingTimestamp,
        ClosingTimestamp = row.ClosingTimestamp,
        CapacitySat = row.CapacitySat,
        NodeId1 = row.SourceNodeId,
        NodeId2 = row.TargetNodeId,
        GossipId = row.GossipId,
        InternalId = row.InternalId,
        RawGossip = row.RawGossip
    };

    public static FeePolicy ToFeePolicy(this ChannelUpdateRow row) => new()
    {
        CltvExpiryDelta = row.CltvExpiryDelta,
        ChannelFlags = row.ChannelFlags,
        FeeBaseMsat = row.FeeBaseMsat,
        FeeProportionalMillionths = row.FeeProportionalMillionths,
        HtlcMinimumMsat = row.HtlcMinimumMsat,
        HtlcMaximumMsat = row.HtlcMaximumMsat
    };

    public static ChannelUpdate ToChannelUpdate(this ChannelUpdateRow row) => new()
    {
        Scid = row.Scid,
        Direction = (Direction)row.DirectionValue,
        SourceNodeId = row.SourceNodeId,
        TargetNodeId = row.TargetNodeId,
        ValidFrom = row.ValidFrom,
        ValidTo = row.ValidTo,
        FeePolicy = row.ToFeePolicy(),
        MessageFlags = row.MessageFlags,
        IsFeeUpdate = row.IsFeeUpdate,
        IsTopologyUpdate = row.IsTopologyUpdate,
        GossipId = row.GossipId,
        InternalId = row.InternalId,
        RawGossip = row.RawGossip
    };

    public static Node ToNode(this NodeRow row) => new()
    {
        NodeId = row.NodeId,
        FirstSeen = row.FirstSeen,
        LastSeen = row.LastSeen,
        NumberOfAnnouncements = row.AnnouncementCount
    };

    public static Address ToAddress(this AddressRow row) => new()
    {
        Id = row.Id,
        Type = (NetworkAddressType)row.TypeId,
        Value = row.Address ?? string.Empty,
        Port = row.Port
    };

    public static NodeAnnouncement ToNodeAnnouncement(this NodeAnnouncementRow row, IReadOnlyList<Address>? addresses = null) => new()
    {
        NodeId = row.NodeId,
        Alias = row.Alias,
        RgbColor = row.RgbColor,
        Features = ToHex(row.Features),
        Addresses = addresses ?? Array.Empty<Address>(),
        Timestamp = row.ValidFrom,
        IsDataUpdate = row.IsDataUpdate,
        GossipId = row.GossipId,
        InternalId = row.InternalId,
        RawGossip = row.RawGossip
    };

    public static ChannelClosure ToChannelClosure(this ClosureRow row) => new()
    {
        Scid = row.Scid,
        Type = ParseClosureType(row.TypeText),
        ClosingTxid = row.ClosingTxid,
        ClosingHeight = row.ClosingHeight,
        ClosingTimestamp = row.ClosingTimestamp,
        MiningFeeSat = row.MiningFeeSat,
        SettledBalanceSat = row.SettledBalanceSat,
        Output0Sat = row.Output0Sat,
        Output1Sat = row.Output1Sat,
        BalanceNode1Sat = row.BalanceNode1Sat,
        BalanceNode2Sat = row.BalanceNode2Sat
    };

    private static ClosureType ParseClosureType(string text) =>
        Enum.TryParse<ClosureType>(text, ignoreCase: true, out var value) ? value : ClosureType.Unknown;
}

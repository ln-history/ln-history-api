namespace LN_history.Data.Internal;

// Flat row projections for Dapper materialization. Column names map via
// MatchNamesWithUnderscores (snake_case -> PascalCase). Mapped to domain by RowMappers.

internal sealed class ChannelRow
{
    public long Scid { get; set; }
    public DateTime FundingTimestamp { get; set; }
    public DateTime? ClosingTimestamp { get; set; }
    public long CapacitySat { get; set; }
    public string SourceNodeId { get; set; } = string.Empty;
    public string TargetNodeId { get; set; } = string.Empty;
    public string GossipId { get; set; } = string.Empty;
    public long InternalId { get; set; }
    public byte[]? RawGossip { get; set; }
}

internal sealed class ChannelUpdateRow
{
    public long Scid { get; set; }
    public int DirectionValue { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public int MessageFlags { get; set; }
    public int ChannelFlags { get; set; }
    public int CltvExpiryDelta { get; set; }
    public long HtlcMinimumMsat { get; set; }
    public long FeeBaseMsat { get; set; }
    public long FeeProportionalMillionths { get; set; }
    public long? HtlcMaximumMsat { get; set; }
    public bool IsFeeUpdate { get; set; }
    public bool IsTopologyUpdate { get; set; }
    public string GossipId { get; set; } = string.Empty;
    public long InternalId { get; set; }
    public byte[]? RawGossip { get; set; }
    public string? SourceNodeId { get; set; }
    public string? TargetNodeId { get; set; }
}

internal sealed class NodeRow
{
    public string NodeId { get; set; } = string.Empty;
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public int AnnouncementCount { get; set; }
}

internal sealed class NodeAnnouncementRow
{
    public string NodeId { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public string? RgbColor { get; set; }
    public byte[]? Features { get; set; }
    public DateTime ValidFrom { get; set; }
    public bool IsDataUpdate { get; set; }
    public string GossipId { get; set; } = string.Empty;
    public long InternalId { get; set; }
    public byte[]? RawGossip { get; set; }
}

internal sealed class AddressRow
{
    public long Id { get; set; }
    public long? InternalId { get; set; }
    public int TypeId { get; set; }
    public string? Address { get; set; }
    public int Port { get; set; }
}

internal sealed class ChannelUpdateCountsRow
{
    public int Dir0 { get; set; }
    public int Dir1 { get; set; }
}

internal sealed class ClosureRow
{
    public long Scid { get; set; }
    public string TypeText { get; set; } = string.Empty;
    public long MiningFeeSat { get; set; }
    public string ClosingTxid { get; set; } = string.Empty;
    public int ClosingHeight { get; set; }
    public DateTime ClosingTimestamp { get; set; }
    public long SettledBalanceSat { get; set; }
    public long? Output0Sat { get; set; }
    public long? Output1Sat { get; set; }
    public long? BalanceNode1Sat { get; set; }
    public long? BalanceNode2Sat { get; set; }
}

using LN_History.Model.Enums;

namespace LN_history.Api.Dto;

public sealed class BlockDto
{
    public string BlockHash { get; set; } = string.Empty;
    public int BlockHeight { get; set; }
    public DateTime Timestamp { get; set; }
    public long SpaceBytes { get; set; }
    public long SubsidySat { get; set; }
    public long TxFees { get; set; }
}

/// <summary>A single chronological event in a snapshot diff. <see cref="Data"/> is a
/// <see cref="ChannelDto"/>, <see cref="ChannelUpdateDto"/> or <see cref="NodeAnnouncementDto"/>.</summary>
public sealed class GossipEventDto
{
    public GossipEventType EventType { get; set; }
    public DateTime Timestamp { get; set; }
    public object Data { get; set; } = null!;
}

public sealed class PagedResultDto<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public long Total { get; set; }
    public int Limit { get; set; }
    public int Offset { get; set; }
}

// --- stats ---

public sealed class ChannelStatDto
{
    public long Scid { get; set; }
    public string ScidStr { get; set; } = string.Empty;
    public long CapacitySat { get; set; }
    public DateTime FundingTimestamp { get; set; }
    public DateTime? ClosingTimestamp { get; set; }
    public int UpdateCount { get; set; }
}

public sealed class NodeStatDto
{
    public string NodeId { get; set; } = string.Empty;
    public int ChannelCount { get; set; }
    public int AnnouncementCount { get; set; }
    public long TotalCapacitySat { get; set; }
}

public sealed class NetworkStatsDto
{
    public DateTime? At { get; set; }
    public long NodeCount { get; set; }
    public long ChannelCount { get; set; }
    public long TotalCapacitySat { get; set; }
}

public sealed class ClosureTypeCountDto
{
    public string ClosureType { get; set; } = string.Empty;
    public long Count { get; set; }
    public long TotalMiningFeeSat { get; set; }
}

public sealed class ClosureStatsDto
{
    public long Total { get; set; }
    public long TotalMiningFeeSat { get; set; }
    public IReadOnlyList<ClosureTypeCountDto> ByType { get; set; } = Array.Empty<ClosureTypeCountDto>();
}

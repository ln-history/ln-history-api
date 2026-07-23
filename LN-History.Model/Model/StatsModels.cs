using LN_History.Model.Enums;

namespace LN_History.Model;

/// <summary>A channel plus its rankable metrics (used by stats/leaderboards).</summary>
public record ChannelStat(
    long Scid,
    long CapacitySat,
    DateTime FundingTimestamp,
    DateTime? ClosingTimestamp,
    int UpdateCount);

/// <summary>A node plus its rankable metrics.</summary>
public record NodeStat(
    string NodeId,
    int ChannelCount,
    int AnnouncementCount,
    long TotalCapacitySat);

/// <summary>Network-wide aggregate counts, either currently (At = null) or at a point in time.</summary>
public record NetworkStats(
    DateTime? At,
    long NodeCount,
    long ChannelCount,
    long TotalCapacitySat);

public record ClosureTypeCount(ClosureType Type, long Count, long TotalMiningFeeSat);

/// <summary>Aggregated channel-closure statistics over an optional time window.</summary>
public record ClosureStats(
    long Total,
    long TotalMiningFeeSat,
    IReadOnlyList<ClosureTypeCount> ByType);

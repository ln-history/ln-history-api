namespace LN_History.Model;

/// <summary>
/// A channel's scid and its funding capacity. Lightweight projection used to attach capacities
/// to channels reconstructed from raw gossip (channel_announcement carries no capacity_sat).
/// </summary>
public record ChannelCapacity(long Scid, long CapacitySat);

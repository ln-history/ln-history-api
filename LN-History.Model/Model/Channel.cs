using LN_History.Model.Enums;

namespace LN_History.Model;

/// <summary>
/// A Lightning channel (from a channel_announcement, type 256), optionally enriched
/// with node information, per-direction policies and closure data.
/// </summary>
public class Channel
{
    public long Scid { get; set; }

    public DateTime FundingTimestamp { get; set; }
    public DateTime? ClosingTimestamp { get; set; }

    /// <summary>Closure details when the channel is closed; null while open.</summary>
    public ChannelClosure? Closure { get; set; }

    public long CapacitySat { get; set; }

    /// <summary>node_1 pubkey (source_node_id).</summary>
    public string NodeId1 { get; set; } = string.Empty;

    /// <summary>node_2 pubkey (target_node_id).</summary>
    public string NodeId2 { get; set; } = string.Empty;

    /// <summary>Full node_1 record; populated only when node expansion is requested.</summary>
    public Node? Node1 { get; set; }

    /// <summary>Full node_2 record; populated only when node expansion is requested.</summary>
    public Node? Node2 { get; set; }

    /// <summary>
    /// Per-direction current policy + total update count. Populated only on single-channel
    /// and history lookups; null in list contexts.
    /// </summary>
    public IReadOnlyDictionary<Direction, DirectionPolicy>? Policies { get; set; }

    public string GossipId { get; set; } = string.Empty;
    public long InternalId { get; set; }

    /// <summary>Full raw gossip envelope; null unless explicitly requested.</summary>
    public byte[]? RawGossip { get; set; }
}

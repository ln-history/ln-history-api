using LN_History.Model.Enums;

namespace LN_History.Model;

/// <summary>
/// A single channel_update message (type 258), one link in the temporal chain for a
/// (scid, direction) pair.
/// </summary>
public class ChannelUpdate
{
    public long Scid { get; set; }
    public Direction Direction { get; set; }

    /// <summary>Source node pubkey for this direction (from the channel), when available.</summary>
    public string? SourceNodeId { get; set; }

    /// <summary>Target node pubkey for this direction (from the channel), when available.</summary>
    public string? TargetNodeId { get; set; }

    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    public FeePolicy FeePolicy { get; set; } = new();

    /// <summary>Raw message_flags bitfield (rendered as a binary string by the API).</summary>
    public int MessageFlags { get; set; }

    public bool IsTopologyUpdate { get; set; }
    public bool IsFeeUpdate { get; set; }

    public string GossipId { get; set; } = string.Empty;
    public long InternalId { get; set; }

    /// <summary>Full raw gossip envelope; null unless explicitly requested.</summary>
    public byte[]? RawGossip { get; set; }
}

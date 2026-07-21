namespace LN_History.Model;

/// <summary>
/// A node_announcement message (type 257): a node's advertised metadata at a point in time.
/// </summary>
public class NodeAnnouncement
{
    public string NodeId { get; set; } = string.Empty;
    public string? Alias { get; set; }

    /// <summary>Hex RGB colour, e.g. "ff0000".</summary>
    public string? RgbColor { get; set; }

    /// <summary>Hex-encoded feature bits.</summary>
    public string? Features { get; set; }

    public IReadOnlyList<Address> Addresses { get; set; } = Array.Empty<Address>();

    /// <summary>Sender timestamp (valid_from) of this announcement.</summary>
    public DateTime Timestamp { get; set; }

    public bool IsDataUpdate { get; set; }

    public string GossipId { get; set; } = string.Empty;
    public long InternalId { get; set; }

    /// <summary>Full raw gossip envelope; null unless explicitly requested.</summary>
    public byte[]? RawGossip { get; set; }
}

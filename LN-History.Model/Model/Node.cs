namespace LN_History.Model;

/// <summary>
/// A Lightning node identified by its public key, with aggregate counts and its
/// announcement(s).
/// </summary>
public class Node
{
    public string NodeId { get; set; } = string.Empty;

    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }

    /// <summary>Currently-open channel degree (default).</summary>
    public int NumberOfChannels { get; set; }

    /// <summary>All-time channel degree; populated only when explicitly requested.</summary>
    public int? NumberOfChannelsAllTime { get; set; }

    /// <summary>Total node_announcements ever seen (from <c>nodes.announcement_count</c>).</summary>
    public int NumberOfAnnouncements { get; set; }

    /// <summary>
    /// Current active announcement on the single-node endpoint; the full chain on /history.
    /// </summary>
    public IReadOnlyList<NodeAnnouncement> Announcements { get; set; } = Array.Empty<NodeAnnouncement>();
}

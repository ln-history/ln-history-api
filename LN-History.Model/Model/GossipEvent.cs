using LN_History.Model.Enums;

namespace LN_History.Model;

/// <summary>
/// A single, chronologically-ordered event in a snapshot diff timeline. <see cref="Data"/>
/// holds a <see cref="Channel"/>, <see cref="ChannelUpdate"/> or <see cref="NodeAnnouncement"/>
/// according to <see cref="EventType"/>.
/// </summary>
public class GossipEvent
{
    public GossipEventType EventType { get; set; }
    public DateTime Timestamp { get; set; }
    public object Data { get; set; } = null!;

    public static GossipEvent ForChannel(Channel channel) =>
        new() { EventType = GossipEventType.Channel, Timestamp = channel.FundingTimestamp, Data = channel };

    public static GossipEvent ForChannelUpdate(ChannelUpdate update) =>
        new() { EventType = GossipEventType.ChannelUpdate, Timestamp = update.ValidFrom, Data = update };

    public static GossipEvent ForNode(NodeAnnouncement announcement) =>
        new() { EventType = GossipEventType.Node, Timestamp = announcement.Timestamp, Data = announcement };
}

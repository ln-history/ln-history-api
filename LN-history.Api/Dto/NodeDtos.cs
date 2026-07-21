namespace LN_history.Api.Dto;

public sealed class AddressTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed class AddressDto
{
    public long Id { get; set; }
    public AddressTypeDto Network { get; set; } = new();
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
}

public sealed class NodeAnnouncementDto
{
    public string NodeId { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public string? RgbColor { get; set; }
    public string? Features { get; set; }
    public IReadOnlyList<AddressDto> Addresses { get; set; } = Array.Empty<AddressDto>();
    public DateTime Timestamp { get; set; }
    public bool IsDataUpdate { get; set; }
    public string GossipId { get; set; } = string.Empty;
    public long InternalId { get; set; }
    public byte[]? RawGossip { get; set; }
}

public sealed class NodeDto
{
    public string NodeId { get; set; } = string.Empty;
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public int NumberOfChannels { get; set; }
    public int? NumberOfChannelsAllTime { get; set; }
    public int NumberOfAnnouncements { get; set; }
    public IReadOnlyList<NodeAnnouncementDto> Announcements { get; set; } = Array.Empty<NodeAnnouncementDto>();
}

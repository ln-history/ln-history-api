namespace LN_history.Api.Model;

public class NodeDto
{
    public string NodeId { get; set; } = string.Empty;
    public string? Alias { get; set; }
    public string? RgbColor { get; set; }
    
    // Stored as byte[] (BYTEA) in DB. 
    // JSON will serialize this as Base64 by default.
    public byte[]? Features { get; set; }

    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public DateTime LastUpdated { get; set; }

    // Navigation property for the relationship
    public List<NodeAddressDto> Addresses { get; set; } = new();
}
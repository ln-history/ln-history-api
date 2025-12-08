namespace LN_history.Api.Model;

public class NodeAddressDto
{
    public string Type { get; set; } = string.Empty; // "IPv4", "TorV3", etc.
    public string Address { get; set; } = string.Empty;
    public int Port { get; set; }
}
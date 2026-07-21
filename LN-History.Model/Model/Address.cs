using LN_History.Model.Enums;

namespace LN_History.Model;

/// <summary>
/// A network address advertised by a node_announcement (from <c>node_addresses</c>).
/// </summary>
public class Address
{
    public long Id { get; set; }
    public NetworkAddressType Type { get; set; }
    public string Value { get; set; } = string.Empty;
    public int Port { get; set; }
}

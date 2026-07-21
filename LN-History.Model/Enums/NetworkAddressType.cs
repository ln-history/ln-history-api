namespace LN_History.Model.Enums;

/// <summary>
/// Network address descriptor type (BOLT 7). Values match the ids in the
/// <c>address_types</c> reference table.
/// </summary>
public enum NetworkAddressType
{
    IPv4 = 1,
    IPv6 = 2,
    TorV2 = 3,
    TorV3 = 4,
    Dns = 5
}

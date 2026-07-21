namespace Bitcoin.Data.Settings;

/// <summary>
/// Connection settings for the Bitcoin Core JSON-RPC endpoint.
/// Bound from the "Bitcoind" configuration section.
/// </summary>
public class BitcoinRpcSettings
{
    public string RpcHost { get; set; } = string.Empty;
    public int RpcPort { get; set; }
    public string RpcUser { get; set; } = string.Empty;
    public string RpcPassword { get; set; } = string.Empty;
}

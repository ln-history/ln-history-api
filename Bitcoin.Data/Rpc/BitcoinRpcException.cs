namespace Bitcoin.Data.Rpc;

/// <summary>Raised when the Bitcoin Core JSON-RPC endpoint returns an error object.</summary>
public class BitcoinRpcException : Exception
{
    public int Code { get; }

    public BitcoinRpcException(int code, string message) : base($"Bitcoin RPC error {code}: {message}")
    {
        Code = code;
    }

    // Common Bitcoin Core error codes used to distinguish "not found" from real failures.
    public const int RpcInvalidParameter = -8;   // e.g. block height out of range
    public const int RpcInvalidAddressOrKey = -5; // e.g. no such transaction / block not found
}

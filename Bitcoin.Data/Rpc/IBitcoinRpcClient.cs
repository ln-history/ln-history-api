namespace Bitcoin.Data.Rpc;

/// <summary>Thin client over the Bitcoin Core JSON-RPC endpoint (only the methods this API needs).</summary>
public interface IBitcoinRpcClient
{
    Task<long> GetBlockCountAsync(CancellationToken cancellationToken);

    /// <summary>Block hash for a height, or null if the height is out of range.</summary>
    Task<string?> GetBlockHashAsync(long height, CancellationToken cancellationToken);

    /// <summary>Block header time (unix seconds) for a height, or null if out of range.</summary>
    Task<long?> GetBlockTimeAsync(long height, CancellationToken cancellationToken);

    /// <summary>Full block statistics by height, or null if not found.</summary>
    Task<BlockSummary?> GetBlockByHeightAsync(long height, CancellationToken cancellationToken);

    /// <summary>Full block statistics by hash, or null if not found.</summary>
    Task<BlockSummary?> GetBlockByHashAsync(string hash, CancellationToken cancellationToken);

    /// <summary>Raw transaction bytes for a txid, or null if not found.</summary>
    Task<byte[]?> GetRawTransactionAsync(string txid, CancellationToken cancellationToken);
}

/// <summary>Public projection of the block fields the API exposes.</summary>
public sealed record BlockSummary(string Hash, long Height, long TimeUnix, long SizeBytes, long SubsidySat, long TotalFeeSat, long TxCount);

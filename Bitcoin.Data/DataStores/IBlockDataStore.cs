using LN_History.Model;

namespace Bitcoin.Data.DataStores;

public interface IBlockDataStore
{
    Task<Block?> GetByHeightAsync(long height, CancellationToken cancellationToken);

    Task<Block?> GetByHashAsync(string hash, CancellationToken cancellationToken);

    /// <summary>The last block mined at or before <paramref name="timestamp"/>, or null if before genesis.</summary>
    Task<Block?> GetByTimestampAsync(DateTime timestamp, CancellationToken cancellationToken);

    /// <summary>Raw transaction bytes for a txid, or null if not found.</summary>
    Task<byte[]?> GetRawTransactionAsync(string txid, CancellationToken cancellationToken);
}

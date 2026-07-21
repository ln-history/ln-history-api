using Bitcoin.Data.Rpc;
using LN_History.Model;

namespace Bitcoin.Data.DataStores;

public class BlockDataStore : IBlockDataStore
{
    private readonly IBitcoinRpcClient _rpc;

    public BlockDataStore(IBitcoinRpcClient rpc)
    {
        _rpc = rpc;
    }

    public async Task<Block?> GetByHeightAsync(long height, CancellationToken cancellationToken)
    {
        // Block heights fit in int32; anything larger cannot exist (and would trip the
        // node's "JSON integer out of range" error rather than a clean not-found).
        if (height < 0 || height > int.MaxValue) return null;
        var summary = await _rpc.GetBlockByHeightAsync(height, cancellationToken);
        return summary is null ? null : ToBlock(summary);
    }

    public async Task<Block?> GetByHashAsync(string hash, CancellationToken cancellationToken)
    {
        var summary = await _rpc.GetBlockByHashAsync(hash, cancellationToken);
        return summary is null ? null : ToBlock(summary);
    }

    public async Task<Block?> GetByTimestampAsync(DateTime timestamp, CancellationToken cancellationToken)
    {
        var target = ToUnixSeconds(timestamp);

        var genesisTime = await _rpc.GetBlockTimeAsync(0, cancellationToken);
        if (genesisTime is null || target < genesisTime) return null;

        // Binary search for the highest height whose block time is <= target.
        // Note: block times are only near-monotonic, so the result can be off by a
        // block or two around clock-skewed blocks; acceptable for "block in force at T".
        long lo = 0;
        long hi = await _rpc.GetBlockCountAsync(cancellationToken);

        while (lo < hi)
        {
            var mid = lo + (hi - lo + 1) / 2;
            var midTime = await _rpc.GetBlockTimeAsync(mid, cancellationToken);
            if (midTime is not null && midTime <= target)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return await GetByHeightAsync(lo, cancellationToken);
    }

    public Task<byte[]?> GetRawTransactionAsync(string txid, CancellationToken cancellationToken) =>
        _rpc.GetRawTransactionAsync(txid, cancellationToken);

    private static Block ToBlock(BlockSummary s) => new()
    {
        BlockHash = s.Hash,
        BlockHeight = (int)s.Height,
        Timestamp = DateTimeOffset.FromUnixTimeSeconds(s.TimeUnix).UtcDateTime,
        SpaceBytes = s.SizeBytes,
        SubsidySat = s.SubsidySat,
        TxFees = s.TotalFeeSat,
        TxCount = (int)s.TxCount
    };

    private static long ToUnixSeconds(DateTime timestamp)
    {
        var utc = timestamp.Kind switch
        {
            DateTimeKind.Utc => timestamp,
            DateTimeKind.Local => timestamp.ToUniversalTime(),
            _ => DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)
        };
        return new DateTimeOffset(utc).ToUnixTimeSeconds();
    }
}

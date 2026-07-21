using Bitcoin.Data.DataStores;
using LN_History.Model;

namespace LN_history.Core.Services;

public class BlockService : IBlockService
{
    private readonly IBlockDataStore _blocks;

    public BlockService(IBlockDataStore blocks)
    {
        _blocks = blocks;
    }

    public Task<Block?> GetByHeightAsync(long height, CancellationToken cancellationToken) =>
        _blocks.GetByHeightAsync(height, cancellationToken);

    public Task<Block?> GetByHashAsync(string hash, CancellationToken cancellationToken) =>
        _blocks.GetByHashAsync(hash, cancellationToken);

    public Task<Block?> GetByTimestampAsync(DateTime timestamp, CancellationToken cancellationToken) =>
        _blocks.GetByTimestampAsync(timestamp, cancellationToken);
}

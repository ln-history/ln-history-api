using LN_History.Model;

namespace LN_history.Core.Services;

public interface IBlockService
{
    Task<Block?> GetByHeightAsync(long height, CancellationToken cancellationToken);

    Task<Block?> GetByHashAsync(string hash, CancellationToken cancellationToken);

    /// <summary>The last block mined at or before <paramref name="timestamp"/>.</summary>
    Task<Block?> GetByTimestampAsync(DateTime timestamp, CancellationToken cancellationToken);
}

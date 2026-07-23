using LN_History.Model;

namespace LN_history.Core.Services;

public interface ISnapshotService
{
    Task<byte[]> GetSnapshotAsync(DateTime timestamp, bool withUpdates, CancellationToken cancellationToken);

    Task<byte[]> GetDiffRawAsync(DateTime start, DateTime end, CancellationToken cancellationToken);

    Task<IReadOnlyList<GossipEvent>> GetDiffEventsAsync(DateTime start, DateTime end, CancellationToken cancellationToken);
}

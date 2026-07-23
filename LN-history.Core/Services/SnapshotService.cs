using LN_history.Data.DataStores;
using LN_History.Model;

namespace LN_history.Core.Services;

public class SnapshotService : ISnapshotService
{
    private readonly ISnapshotDataStore _snapshots;

    public SnapshotService(ISnapshotDataStore snapshots)
    {
        _snapshots = snapshots;
    }

    public Task<byte[]> GetSnapshotAsync(DateTime timestamp, bool withUpdates, CancellationToken cancellationToken) =>
        _snapshots.GetSnapshotRawAsync(timestamp, withUpdates, cancellationToken);

    public Task<byte[]> GetDiffRawAsync(DateTime start, DateTime end, CancellationToken cancellationToken) =>
        _snapshots.GetDiffRawAsync(start, end, cancellationToken);

    public Task<IReadOnlyList<GossipEvent>> GetDiffEventsAsync(DateTime start, DateTime end, CancellationToken cancellationToken) =>
        _snapshots.GetDiffEventsAsync(start, end, cancellationToken);
}

using LN_History.Model;

namespace LN_history.Data.DataStores;

public interface IClosureDataStore
{
    /// <summary>Closure details for a channel by scid, or null if the channel is not closed.</summary>
    Task<ChannelClosure?> GetByScidAsync(long scid, CancellationToken cancellationToken);
}

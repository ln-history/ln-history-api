namespace LN_history.Cache.Services;

public interface ICacheService
{
    Task<bool> CheckIfObjectExists(string bucketName, string objectName, CancellationToken cancellationToken);
    
    // Task StoreGraphAsync(string bucketId, string objectName, LightningFastGraph graph, CancellationToken cancellationToken);

    // Task<LightningFastGraph> GetGraphJsonAsync(string bucketId, string objectName, CancellationToken cancellationToken);

    Task<byte[]?> GetGraphTopologyUsingRpcAsync(string bucketName, string objectName, CancellationToken cancellationToken);

    Task StoreGraphTopologyUsingRpcAsync(byte[] data, string bucketName, string objectName,
        CancellationToken cancellationToken);
}
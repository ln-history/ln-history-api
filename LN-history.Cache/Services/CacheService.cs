using System.Text;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace LN_history.Cache.Services;

public class CacheService : ICacheService
{
    private readonly IMinioClient _minioClient;
    private readonly ILogger<CacheService> _logger;

    public CacheService(IMinioClient minioClient, ILogger<CacheService> logger)
    {
        _minioClient = minioClient;
        _logger = logger;
    }

    public async Task<bool> CheckIfObjectExists(string bucketName, string objectName, CancellationToken cancellationToken)
    {
        try
        {
            var statBucketArgs = new BucketExistsArgs()
                .WithBucket(bucketName);
            
            // Check if the bucket exists
            var isBucketExisting = await _minioClient.BucketExistsAsync(statBucketArgs, cancellationToken);

            if (!isBucketExisting)
            {
                _logger.LogWarning($"Bucket {bucketName} does not exist.");
                return false; // Bucket does not exist
            }

            // Check if the object exists in the bucket
            try
            {
                var statObjectArgs = new StatObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(objectName);

                await _minioClient.StatObjectAsync(statObjectArgs, cancellationToken);

                return true;
            }
            catch (ObjectNotFoundException)
            {
                return false;
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, $"Check if object exists failed for bucketName {bucketName}, objectName {objectName}");
            return false; // Return false in case of any unexpected error
        }
    }
    
    public async Task<byte[]?> GetGraphTopologyUsingRpcAsync(string bucketName, string objectName, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Start retrieving binary data from bucket {BucketId}, object {ObjectName}", 
                bucketName, objectName);

            using var memStream = new MemoryStream();

            var getObjectArgs = new GetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectName)
                .WithCallbackStream(stream =>
                {
                    // ReSharper disable once AccessToDisposedClosure
                    stream.CopyTo(memStream);
                });

            await _minioClient.GetObjectAsync(getObjectArgs, cancellationToken);

            _logger.LogInformation("Successfully retrieved data of size {Size} bytes from {Bucket}/{Object}",
                memStream.Length, bucketName, objectName);

            return memStream.ToArray();
        }
        catch (ObjectNotFoundException)
        {
            _logger.LogWarning("Object not found in bucket {BucketId}, object {ObjectName}",
                bucketName, objectName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving data from {BucketId}/{ObjectName}",
                bucketName, objectName);
            throw;
        }
    }

    
    public async Task StoreGraphTopologyUsingRpcAsync(byte[] data, string bucketName, string objectName, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Start storing binary data to bucket {BucketId}, object {ObjectName}", 
                bucketName, objectName);

            using var memStream = new MemoryStream(data);

            var putObjectArgs = new PutObjectArgs()
                .WithBucket(bucketName)
                .WithObject(objectName)
                .WithStreamData(memStream)
                .WithObjectSize(data.Length)
                .WithContentType("application/octet-stream");

            await _minioClient.PutObjectAsync(putObjectArgs, cancellationToken);

            _logger.LogInformation("Successfully stored data of size {Size} bytes to {Bucket}/{Object}",
                data.Length, bucketName, objectName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing data to {BucketId}/{ObjectName}",
                bucketName, objectName);
            throw;
        }
    }
}

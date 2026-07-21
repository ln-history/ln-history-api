using LN_history.Cache.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minio;

namespace LN_history.Cache;

public static class ServiceCollectionExtension
{
    public static void AddCaching(this IServiceCollection serviceCollection, IConfiguration configuration)
    {
        var endpoint = configuration.GetSection("MinIO:Endpoint").Value;
        var accessKey = configuration.GetSection("MinIO:AccessKey").Value;
        var secretKey = configuration.GetSection("MinIO:SecretKey").Value;
        
        serviceCollection.AddSingleton<IMinioClient>(sp => new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithTimeout(60 * 1_000) // 60 seconds
            .Build());
        
        serviceCollection.AddScoped<ICacheService, CacheService>();
    }
}
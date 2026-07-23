using Microsoft.Extensions.DependencyInjection;

namespace LN_history.Api;

public static class ServiceCollectionExtension
{
    /// <summary>
    /// Registers API-layer services (DTO mappers are registered here as they are added).
    /// </summary>
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        return services;
    }
}

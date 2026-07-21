using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LN_history.Core;

public static class ServiceCollectionExtension
{
    /// <summary>
    /// Registers Core-layer services (channel, node, snapshot, block, stats).
    /// Concrete services are registered here as they are added (Phase 4).
    /// </summary>
    public static IServiceCollection AddLightningNetworkServices(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}

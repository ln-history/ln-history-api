using LN_history.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace LN_history.Core;

public static class ServiceCollectionExtension
{
    /// <summary>
    /// Registers Core-layer services (channel, node, snapshot, block, stats) that orchestrate
    /// the data stores and Bitcoin RPC access.
    /// </summary>
    public static IServiceCollection AddLightningNetworkServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IChannelService, ChannelService>();
        services.AddScoped<INodeService, NodeService>();
        services.AddScoped<ISnapshotService, SnapshotService>();
        services.AddScoped<IBlockService, BlockService>();
        services.AddScoped<IStatsService, StatsService>();

        return services;
    }
}

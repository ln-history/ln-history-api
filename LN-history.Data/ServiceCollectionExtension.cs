using LN_history.Data.DataStores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LN_history.Data;

public static class ServiceCollectionExtension
{
    /// <summary>
    /// Registers PostgreSQL access via a singleton <see cref="NpgsqlDataSource"/> and the data stores.
    /// Data stores open short-lived connections from the data source per query.
    /// </summary>
    public static IServiceCollection AddLnHistoryDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("Connection string 'PostgreSQL' is missing.");

        services.AddNpgsqlDataSource(connectionString);

        services.AddScoped<IChannelDataStore, ChannelDataStore>();
        services.AddScoped<INodeDataStore, NodeDataStore>();
        services.AddScoped<IClosureDataStore, ClosureDataStore>();
        services.AddScoped<ISnapshotDataStore, SnapshotDataStore>();
        services.AddScoped<IStatsDataStore, StatsDataStore>();

        return services;
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LN_history.Data;

public static class ServiceCollectionExtension
{
    /// <summary>
    /// Registers PostgreSQL access via a singleton <see cref="NpgsqlDataSource"/>.
    /// Data stores open short-lived connections from the data source per query.
    /// </summary>
    public static IServiceCollection AddLnHistoryDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PostgreSQL")
            ?? throw new InvalidOperationException("Connection string 'PostgreSQL' is missing.");

        services.AddNpgsqlDataSource(connectionString);

        // Data stores are registered here as they are added (Phase 2).
        return services;
    }
}

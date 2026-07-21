using Bitcoin.Data.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Bitcoin.Data;

public static class ServiceCollectionExtension
{
    /// <summary>
    /// Registers Bitcoin Core JSON-RPC access. Binds the "Bitcoind" configuration
    /// section (RPCHost/RPCPort/RPCUser/RPCPassword) to <see cref="BitcoinRpcSettings"/>.
    /// The RPC client and block data store are added in a later phase.
    /// </summary>
    public static IServiceCollection AddBitcoinNode(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Bitcoind");
        if (!section.Exists())
        {
            throw new InvalidOperationException("Configuration section 'Bitcoind' is missing.");
        }

        services.Configure<BitcoinRpcSettings>(options =>
        {
            options.RpcHost = section["RPCHost"] ?? string.Empty;
            options.RpcPort = int.TryParse(section["RPCPort"], out var port) ? port : 8332;
            options.RpcUser = section["RPCUser"] ?? string.Empty;
            options.RpcPassword = section["RPCPassword"] ?? string.Empty;
        });

        return services;
    }
}

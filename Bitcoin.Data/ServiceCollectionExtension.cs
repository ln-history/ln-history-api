using System.Net.Http.Headers;
using System.Text;
using Bitcoin.Data.DataStores;
using Bitcoin.Data.Rpc;
using Bitcoin.Data.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bitcoin.Data;

public static class ServiceCollectionExtension
{
    /// <summary>
    /// Registers Bitcoin Core JSON-RPC access: binds the "Bitcoind" configuration section
    /// (RPCHost/RPCPort/RPCUser/RPCPassword), a typed <see cref="BitcoinRpcClient"/> with HTTP
    /// Basic auth, and the block data store.
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

        services.AddHttpClient<IBitcoinRpcClient, BitcoinRpcClient>((serviceProvider, http) =>
        {
            var settings = serviceProvider.GetRequiredService<IOptions<BitcoinRpcSettings>>().Value;
            http.BaseAddress = new Uri($"http://{settings.RpcHost}:{settings.RpcPort}/");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.RpcUser}:{settings.RpcPassword}"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            http.Timeout = TimeSpan.FromSeconds(30);
        });

        services.AddScoped<IBlockDataStore, BlockDataStore>();

        return services;
    }
}

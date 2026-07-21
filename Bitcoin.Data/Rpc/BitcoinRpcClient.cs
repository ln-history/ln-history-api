using System.Text;
using System.Text.Json;

namespace Bitcoin.Data.Rpc;

public class BitcoinRpcClient : IBitcoinRpcClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly object[] StatsFields =
        { "blockhash", "height", "time", "total_size", "subsidy", "totalfee", "txs" };

    public BitcoinRpcClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<long> GetBlockCountAsync(CancellationToken cancellationToken)
    {
        var result = await InvokeAsync("getblockcount", Array.Empty<object?>(), cancellationToken);
        return result.GetInt64();
    }

    public async Task<string?> GetBlockHashAsync(long height, CancellationToken cancellationToken)
    {
        var result = await TryInvokeAsync("getblockhash", new object?[] { height }, cancellationToken);
        return result?.GetString();
    }

    public async Task<long?> GetBlockTimeAsync(long height, CancellationToken cancellationToken)
    {
        var result = await TryInvokeAsync("getblockstats", new object?[] { height, new object[] { "time" } }, cancellationToken);
        return result is { } element ? element.GetProperty("time").GetInt64() : null;
    }

    public Task<BlockSummary?> GetBlockByHeightAsync(long height, CancellationToken cancellationToken) =>
        GetBlockAsync(height, cancellationToken);

    public Task<BlockSummary?> GetBlockByHashAsync(string hash, CancellationToken cancellationToken) =>
        GetBlockAsync(hash, cancellationToken);

    private async Task<BlockSummary?> GetBlockAsync(object hashOrHeight, CancellationToken cancellationToken)
    {
        var result = await TryInvokeAsync("getblockstats", new object?[] { hashOrHeight, StatsFields }, cancellationToken);
        if (result is not { } element) return null;

        var stats = element.Deserialize<BlockStats>(JsonOptions)!;
        return new BlockSummary(stats.Blockhash, stats.Height, stats.Time, stats.TotalSize, stats.Subsidy, stats.Totalfee, stats.Txs);
    }

    public async Task<byte[]?> GetRawTransactionAsync(string txid, CancellationToken cancellationToken)
    {
        var result = await TryInvokeAsync("getrawtransaction", new object?[] { txid, false }, cancellationToken);
        var hex = result?.GetString();
        return string.IsNullOrEmpty(hex) ? null : Convert.FromHexString(hex);
    }

    /// <summary>Invokes an RPC method, returning null when the node reports a not-found style error.</summary>
    private async Task<JsonElement?> TryInvokeAsync(string method, object?[] parameters, CancellationToken cancellationToken)
    {
        try
        {
            return await InvokeAsync(method, parameters, cancellationToken);
        }
        catch (BitcoinRpcException ex) when (ex.Code is BitcoinRpcException.RpcInvalidParameter or BitcoinRpcException.RpcInvalidAddressOrKey)
        {
            return null;
        }
    }

    private async Task<JsonElement> InvokeAsync(string method, object?[] parameters, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(
            new { jsonrpc = "1.0", id = "ln-history-api", method, @params = parameters }, JsonOptions);

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(string.Empty, content, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

        RpcEnvelope? envelope;
        try
        {
            envelope = await JsonSerializer.DeserializeAsync<RpcEnvelope>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            // Non-JSON body (e.g. 401 Unauthorized) — surface the HTTP failure.
            response.EnsureSuccessStatusCode();
            throw;
        }

        if (envelope is null) throw new BitcoinRpcException(0, "Empty RPC response.");
        if (envelope.Error is not null) throw new BitcoinRpcException(envelope.Error.Code, envelope.Error.Message);
        return envelope.Result;
    }
}

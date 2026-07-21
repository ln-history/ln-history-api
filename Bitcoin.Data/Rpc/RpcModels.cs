using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bitcoin.Data.Rpc;

internal sealed class RpcEnvelope
{
    [JsonPropertyName("result")]
    public JsonElement Result { get; set; }

    [JsonPropertyName("error")]
    public RpcError? Error { get; set; }
}

internal sealed class RpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

/// <summary>Subset of getblockstats output needed to build a <see cref="LN_History.Model.Block"/>.</summary>
internal sealed class BlockStats
{
    [JsonPropertyName("blockhash")]
    public string Blockhash { get; set; } = string.Empty;

    [JsonPropertyName("height")]
    public long Height { get; set; }

    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("total_size")]
    public long TotalSize { get; set; }

    [JsonPropertyName("subsidy")]
    public long Subsidy { get; set; }

    [JsonPropertyName("totalfee")]
    public long Totalfee { get; set; }
}

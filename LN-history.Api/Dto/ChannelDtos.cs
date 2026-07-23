using System.Text.Json.Serialization;

namespace LN_history.Api.Dto;

public sealed class FeePolicyDto
{
    public int CltvExpiryDelta { get; set; }

    /// <summary>channel_flags bitfield as a binary string.</summary>
    public string ChannelFlags { get; set; } = string.Empty;

    public long FeeBaseMsat { get; set; }
    public long FeeProportionalMillionths { get; set; }
    public long HtlcMinimumMsat { get; set; }
    public long? HtlcMaximumMsat { get; set; }
}

public sealed class DirectionPolicyDto
{
    public FeePolicyDto? FeePolicy { get; set; }
    public int TotalUpdateCount { get; set; }
}

public sealed class ChannelClosureDto
{
    public long Scid { get; set; }
    public string ScidStr { get; set; } = string.Empty;
    public string ClosureType { get; set; } = string.Empty;
    public long MiningFee { get; set; }
    public string Txid { get; set; } = string.Empty;

    /// <summary>Raw closing transaction bytes (base64); null unless requested.</summary>
    public byte[]? Tx { get; set; }
}

public sealed class ChannelUpdateDto
{
    public long Scid { get; set; }
    public string ScidStr { get; set; } = string.Empty;
    public bool Direction { get; set; }
    public string? SourceNodeId { get; set; }
    public string? TargetNodeId { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public FeePolicyDto FeePolicy { get; set; } = new();
    public DateTime Timestamp { get; set; }

    /// <summary>message_flags bitfield as a binary string.</summary>
    public string MessageFlags { get; set; } = string.Empty;

    public bool IsTopologyUpdate { get; set; }
    public bool IsFeeUpdate { get; set; }
    public string GossipId { get; set; } = string.Empty;
    public long InternalId { get; set; }
    public byte[]? RawGossip { get; set; }
}

public sealed class ChannelCapacityDto
{
    public long Scid { get; set; }
    public string ScidStr { get; set; } = string.Empty;
    public long CapacitySat { get; set; }
}

public sealed class ChannelDto
{
    public long Scid { get; set; }
    public string ScidStr { get; set; } = string.Empty;
    public DateTime FundingTimestamp { get; set; }
    public DateTime? ClosingTimestamp { get; set; }
    public ChannelClosureDto? ClosingInformation { get; set; }
    public long CapacitySat { get; set; }

    [JsonPropertyName("node_id_1")]
    public string NodeId1 { get; set; } = string.Empty;

    [JsonPropertyName("node_id_2")]
    public string NodeId2 { get; set; } = string.Empty;

    [JsonPropertyName("node_1")]
    public NodeDto? Node1 { get; set; }

    [JsonPropertyName("node_2")]
    public NodeDto? Node2 { get; set; }

    /// <summary>Per-direction policies keyed "0"/"1". Populated only on single-channel/history lookups.</summary>
    public IReadOnlyDictionary<string, DirectionPolicyDto>? FeePolicies { get; set; }

    public string GossipId { get; set; } = string.Empty;
    public long InternalId { get; set; }
    public byte[]? RawGossip { get; set; }
}

namespace LN_history.Api.Model;

public class ChannelDto
{
    [System.Text.Json.Serialization.JsonIgnore]
    public long Scid { get; set; }

    // Human-readable format (e.g., "800123x12x1")
    public string ShortChannelId => DecodeScid(Scid);

    public string SourceNodeId { get; set; } = string.Empty;
    public string TargetNodeId { get; set; } = string.Empty;
    
    public long CapacitySat { get; set; }
    
    public DateTime? FundingTimestamp { get; set; }
    public DateTime? ClosingTimestamp { get; set; }
    
    public List<ChannelPolicyDto> Policies { get; set; } = new();

    /// <summary>
    /// Converts the 64-bit integer SCID back to the standard Lightning string format.
    /// </summary>
    private static string DecodeScid(long scid)
    {
        var block = scid >> 40;
        var tx = (scid >> 16) & 0xFFFFFF;
        var output = scid & 0xFFFF;
        return $"{block}x{tx}x{output}";
    }
}
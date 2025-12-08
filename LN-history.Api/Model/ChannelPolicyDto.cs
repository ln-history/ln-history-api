namespace LN_history.Api.Model;

public class ChannelPolicyDto
{
    // 0 = Node1 -> Node2
    // 1 = Node2 -> Node1
    // Mapped from Postgres BIT(1) to boolean
    public bool Direction { get; set; }
    
    public long FeeBaseMsat { get; set; }
    public long FeeProportionalMillionths { get; set; }
    public long HtlcMinimumMsat { get; set; }
    public long HtlcMaximumMsat { get; set; }
    
    public DateTime ValidFrom { get; set; }
}
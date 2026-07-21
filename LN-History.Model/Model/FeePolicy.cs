namespace LN_History.Model;

/// <summary>
/// Routing policy carried by a channel_update for one direction of a channel.
/// </summary>
public class FeePolicy
{
    public int CltvExpiryDelta { get; set; }

    /// <summary>Raw channel_flags bitfield (rendered as a binary string by the API).</summary>
    public int ChannelFlags { get; set; }

    public long FeeBaseMsat { get; set; }
    public long FeeProportionalMillionths { get; set; }
    public long HtlcMinimumMsat { get; set; }
    public long? HtlcMaximumMsat { get; set; }
}

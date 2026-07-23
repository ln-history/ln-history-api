using LN_History.Model.Enums;

namespace LN_History.Model;

/// <summary>
/// On-chain closure information for a channel (from <c>channel_closures</c>, enriched
/// with the raw closing transaction from the Bitcoin node when requested).
/// </summary>
public class ChannelClosure
{
    public long Scid { get; set; }
    public ClosureType Type { get; set; }

    public string ClosingTxid { get; set; } = string.Empty;
    public int ClosingHeight { get; set; }
    public DateTime ClosingTimestamp { get; set; }

    public long MiningFeeSat { get; set; }
    public long SettledBalanceSat { get; set; }

    public long? Output0Sat { get; set; }
    public long? Output1Sat { get; set; }
    public long? BalanceNode1Sat { get; set; }
    public long? BalanceNode2Sat { get; set; }

    /// <summary>Raw closing transaction bytes from the Bitcoin node; null unless requested.</summary>
    public byte[]? RawTransaction { get; set; }
}

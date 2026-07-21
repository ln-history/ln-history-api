namespace LN_History.Model;

/// <summary>
/// A Bitcoin block summary, sourced from the Bitcoin Core node.
/// </summary>
public class Block
{
    public string BlockHash { get; set; } = string.Empty;
    public int BlockHeight { get; set; }
    public DateTime Timestamp { get; set; }

    /// <summary>Block size in bytes.</summary>
    public long SpaceBytes { get; set; }

    /// <summary>Block subsidy in satoshis.</summary>
    public long SubsidySat { get; set; }

    /// <summary>Total transaction fees in the block, in satoshis.</summary>
    public long TxFees { get; set; }

    /// <summary>Number of transactions in the block.</summary>
    public int TxCount { get; set; }
}

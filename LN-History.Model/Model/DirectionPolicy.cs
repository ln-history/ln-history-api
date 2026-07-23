namespace LN_History.Model;

/// <summary>
/// A channel's current policy for a single direction, plus the total number of
/// channel_updates ever seen for that direction (from <c>channel_update_counts</c>).
/// </summary>
public class DirectionPolicy
{
    /// <summary>The active fee policy for this direction, or null if the direction never announced.</summary>
    public FeePolicy? FeePolicy { get; set; }

    public int TotalUpdateCount { get; set; }
}

namespace LN_History.Model.Enums;

/// <summary>
/// Directionality of a channel_update (BOLT 7 channel_flags bit 0).
/// Mirrors the <c>direction bit(1)</c> column in the database.
/// </summary>
public enum Direction
{
    /// <summary>0 — node_1 → node_2.</summary>
    NodeOneToNodeTwo = 0,

    /// <summary>1 — node_2 → node_1.</summary>
    NodeTwoToNodeOne = 1
}

namespace LN_History.Model;

/// <summary>
/// Filter for channel list queries. Resolution order: <see cref="OpenAt"/> (open at that
/// instant) wins; else <see cref="CurrentlyOpen"/> (closing_timestamp IS NULL); else all channels.
/// </summary>
public sealed record ChannelListFilter(
    DateTime? OpenAt,
    bool CurrentlyOpen,
    bool IncludeRawGossip,
    int Limit,
    int Offset);

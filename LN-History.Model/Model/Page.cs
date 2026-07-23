namespace LN_History.Model;

/// <summary>
/// A page of results plus the total count for the underlying query.
/// </summary>
public record Page<T>(IReadOnlyList<T> Items, long Total, int Limit, int Offset)
{
    public static Page<T> Empty(int limit, int offset) => new(Array.Empty<T>(), 0, limit, offset);
}

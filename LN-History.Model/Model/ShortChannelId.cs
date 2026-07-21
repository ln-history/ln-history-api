using System.Globalization;

namespace LN_History.Model;

/// <summary>
/// A Lightning short channel id. Wraps the 64-bit integer encoding
/// (<c>block_height &lt;&lt; 40 | tx_index &lt;&lt; 16 | output_index</c>) and converts
/// to/from the canonical human form <c>"BLOCKxTXxOUTPUT"</c> (e.g. <c>"865123x1x0"</c>).
/// </summary>
public readonly struct ShortChannelId : IEquatable<ShortChannelId>
{
    public long Value { get; }

    public ShortChannelId(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "scid must be non-negative.");
        }
        Value = value;
    }

    public int BlockHeight => (int)((Value >> 40) & 0xFFFFFF);
    public int TransactionIndex => (int)((Value >> 16) & 0xFFFFFF);
    public int OutputIndex => (int)(Value & 0xFFFF);

    public static ShortChannelId FromParts(int blockHeight, int transactionIndex, int outputIndex)
    {
        if (blockHeight is < 0 or > 0xFFFFFF) throw new ArgumentOutOfRangeException(nameof(blockHeight));
        if (transactionIndex is < 0 or > 0xFFFFFF) throw new ArgumentOutOfRangeException(nameof(transactionIndex));
        if (outputIndex is < 0 or > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(outputIndex));

        var value = ((long)blockHeight << 40) | ((long)transactionIndex << 16) | (uint)outputIndex;
        return new ShortChannelId(value);
    }

    /// <summary>The canonical <c>"BLOCKxTXxOUTPUT"</c> string form.</summary>
    public override string ToString() => $"{BlockHeight}x{TransactionIndex}x{OutputIndex}";

    /// <summary>
    /// Parses either the raw 64-bit integer form or the <c>"BLOCKxTXxOUTPUT"</c> form.
    /// </summary>
    public static bool TryParse(string? input, out ShortChannelId scid)
    {
        scid = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        input = input.Trim();

        if (input.Contains('x'))
        {
            var parts = input.Split('x');
            if (parts.Length != 3) return false;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var block)) return false;
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var tx)) return false;
            if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var output)) return false;
            if (block > 0xFFFFFF || tx > 0xFFFFFF || output > 0xFFFF) return false;

            scid = FromParts(block, tx, output);
            return true;
        }

        if (long.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            scid = new ShortChannelId(value);
            return true;
        }

        return false;
    }

    public static ShortChannelId Parse(string input) =>
        TryParse(input, out var scid) ? scid : throw new FormatException($"Invalid short channel id: '{input}'.");

    public bool Equals(ShortChannelId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is ShortChannelId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();

    public static implicit operator long(ShortChannelId scid) => scid.Value;
}

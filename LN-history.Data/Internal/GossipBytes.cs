namespace LN_history.Data.Internal;

internal static class GossipBytes
{
    /// <summary>
    /// Concatenates raw gossip blobs into one byte stream. Each blob is a self-describing
    /// envelope (varint length ++ type ++ payload), so concatenation yields a valid stream.
    /// </summary>
    public static byte[] Concat(IEnumerable<byte[]?> blobs)
    {
        using var stream = new MemoryStream();
        foreach (var blob in blobs)
        {
            if (blob is { Length: > 0 })
            {
                stream.Write(blob, 0, blob.Length);
            }
        }
        return stream.ToArray();
    }
}

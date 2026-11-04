using System.Security.Cryptography;
using System.Text;

namespace TaggedMerkle;

/// <summary>Computes tagged Merkle roots using Bitcoin's ordered, duplicate-last tree shape.</summary>
public static class MerkleTree
{
    internal const int HashSize = 32;
    private const int PrefixSize = 2 * HashSize;
    // Branch scratch layout: [prefix: 64 bytes | left hash: 32 bytes | right hash: 32 bytes].
    private const int BranchScratchSize = PrefixSize + 2 * HashSize;
    private const int EncodingBufferSize = 4_096;
    private static readonly UTF8Encoding Utf8 =
        new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

    /// <summary>Returns a fresh 32-byte root in SHA-256 digest order.</summary>
    /// <remarks>
    /// Strings are strict UTF-8 without normalization or a BOM. Empty input returns
    /// 32 zero bytes, matching Bitcoin's empty-tree convention. A singleton returns
    /// its leaf hash. The supplied tag applies to both leaves and branches, using
    /// strict UTF-8 without normalization; an empty tag is valid. Input must not
    /// be modified concurrently during this call.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The array or tag is null.</exception>
    /// <exception cref="ArgumentException">An element is null.</exception>
    /// <exception cref="EncoderFallbackException">An element or the tag contains invalid UTF-16.</exception>
    /// <exception cref="OverflowException">The hash buffer size exceeds Int32 capacity.</exception>
    public static byte[] ComputeRoot(string[] values, string tag)
    {
        ArgumentNullException.ThrowIfNull(values);
        // Checked here so the exception reports "tag" rather than "leafTag".
        ArgumentNullException.ThrowIfNull(tag);
        return ComputeRoot(values, tag, tag);
    }

    /// <summary>Computes a root with separate BIP340 tags for leaves and branches.</summary>
    /// <remarks>
    /// Strings and tags are strict UTF-8 without normalization or a BOM; empty tags
    /// are valid. Empty input returns 32 zero bytes. A singleton returns its leaf
    /// hash. Input must not be modified concurrently during this call.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The array or a tag is null.</exception>
    /// <exception cref="ArgumentException">An element is null.</exception>
    /// <exception cref="EncoderFallbackException">An element or a tag contains invalid UTF-16.</exception>
    /// <exception cref="OverflowException">The hash buffer size exceeds Int32 capacity.</exception>
    public static byte[] ComputeRoot(string[] values, string leafTag, string branchTag)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(leafTag);
        ArgumentNullException.ThrowIfNull(branchTag);
        (byte[] leafPrefix, byte[] branchPrefix) = CreatePrefixes(leafTag, branchTag);
        if (values.Length == 0)
            return new byte[HashSize];

        byte[] level = HashLeaves(values, leafPrefix);
        Span<byte> scratch = stackalloc byte[BranchScratchSize];
        branchPrefix.CopyTo(scratch);
        // Parents overwrite already-consumed children in place:
        // [a, b, c, d, e] -> [ab, cd, ee] -> [abcd, eeee] -> [root].
        // Trailing stale slots beyond the active count are ignored.
        for (int count = values.Length; count > 1; count = ParentCount(count))
            HashLevel(level, count, level, scratch);
        return level.AsSpan(0, HashSize).ToArray();
    }

    /// <summary>Builds an immutable tree whose retained levels support O(log n) proofs.</summary>
    /// <remarks>Input order is preserved. Null elements and malformed UTF-16 are rejected.</remarks>
    /// <exception cref="ArgumentNullException">The array or a tag is null.</exception>
    /// <exception cref="ArgumentException">An element is null.</exception>
    /// <exception cref="EncoderFallbackException">An element or a tag contains invalid UTF-16.</exception>
    /// <exception cref="OverflowException">The hash buffer size exceeds Int32 capacity.</exception>
    public static MerkleTreeSnapshot CreateSnapshot(string[] values, string leafTag, string branchTag)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(leafTag);
        ArgumentNullException.ThrowIfNull(branchTag);
        (byte[] leafPrefix, byte[] branchPrefix) = CreatePrefixes(leafTag, branchTag);

        byte[] level = HashLeaves(values, leafPrefix);
        List<byte[]> levels = [level];
        Span<byte> scratch = stackalloc byte[BranchScratchSize];
        branchPrefix.CopyTo(scratch);
        for (int count = values.Length; count > 1; count = ParentCount(count))
        {
            byte[] parents = new byte[checked(ParentCount(count) * HashSize)];
            HashLevel(level, count, parents, scratch);
            levels.Add(parents);
            level = parents;
        }
        return new MerkleTreeSnapshot(levels.ToArray(), values.Length);
    }

    /// <summary>Number of parents for <paramref name="count"/> children, rounding up.</summary>
    private static int ParentCount(int count) => (count + 1) / 2;

    /// <summary>Validates every element, then hashes each leaf into its slice of a new buffer.</summary>
    private static byte[] HashLeaves(string[] values, ReadOnlySpan<byte> prefix)
    {
        // Validate before allocating the work buffer; never mutate caller-owned data.
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] is null)
                throw new ArgumentException($"Element at index {i} is null.", nameof(values));
        }

        byte[] hashes = new byte[checked(values.Length * HashSize)];
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        // One encoder and buffer serve every leaf: a flushed, completed Convert resets the encoder.
        Encoder encoder = Utf8.GetEncoder();
        Span<byte> buffer = stackalloc byte[EncodingBufferSize];
        for (int i = 0; i < values.Length; i++)
            HashLeaf(values[i], prefix, hasher, encoder, buffer, hashes.AsSpan(i * HashSize, HashSize));
        return hashes;
    }

    /// <summary>
    /// Hashes <paramref name="count"/> children pairwise into parents, pairing an odd last
    /// child with itself. <paramref name="parents"/> may alias <paramref name="children"/>:
    /// parent p is written to slot p only after children 2p and 2p+1 are copied to scratch,
    /// and every later read is from a slot above p.
    /// </summary>
    private static void HashLevel(ReadOnlySpan<byte> children, int count, Span<byte> parents, Span<byte> scratch)
    {
        Span<byte> leftSlot = scratch.Slice(PrefixSize, HashSize);
        Span<byte> rightSlot = scratch.Slice(PrefixSize + HashSize, HashSize);
        for (int left = 0, parent = 0; left < count; left += 2, parent++)
        {
            // With count = 5, child indices are (0,1), (2,3), then (4,4).
            int right = Math.Min(left + 1, count - 1);
            children.Slice(left * HashSize, HashSize).CopyTo(leftSlot);
            children.Slice(right * HashSize, HashSize).CopyTo(rightSlot);
            SHA256.HashData(scratch, parents.Slice(parent * HashSize, HashSize));
        }
    }

    private static void HashLeaf(
        string value, ReadOnlySpan<byte> prefix, IncrementalHash hasher,
        Encoder encoder, Span<byte> buffer, Span<byte> destination)
    {
        hasher.AppendData(prefix);
        // Bounded encoding storage, including for very large individual strings.
        ReadOnlySpan<char> remaining = value.AsSpan();
        bool completed;
        do
        {
            encoder.Convert(remaining, buffer, flush: true,
                out int charsUsed, out int bytesUsed, out completed);
            hasher.AppendData(buffer[..bytesUsed]);
            remaining = remaining[charsUsed..];
        } while (!completed);
        hasher.GetHashAndReset(destination);
    }

    private static (byte[] Leaf, byte[] Branch) CreatePrefixes(string leafTag, string branchTag)
    {
        byte[] leafPrefix = CreatePrefix(leafTag);
        return (leafPrefix, leafTag == branchTag ? leafPrefix : CreatePrefix(branchTag));
    }

    private static byte[] CreatePrefix(string tag)
    {
        // Untagged SHA-256 here is part of the BIP340 tag-prefix definition itself.
        byte[] prefix = new byte[PrefixSize];
        SHA256.HashData(Utf8.GetBytes(tag), prefix.AsSpan(0, HashSize));
        prefix.AsSpan(0, HashSize).CopyTo(prefix.AsSpan(HashSize));
        return prefix;
    }
}

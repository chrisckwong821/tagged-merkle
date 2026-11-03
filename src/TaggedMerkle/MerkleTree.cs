using System.Security.Cryptography;
using System.Text;

namespace TaggedMerkle;

/// <summary>Computes tagged Merkle roots using Bitcoin's ordered, duplicate-last tree shape.</summary>
public static class MerkleTree
{
    private const int HashSize = 32;
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
        ArgumentNullException.ThrowIfNull(tag);
        byte[] prefix = CreatePrefix(tag);
        if (values.Length == 0)
            return new byte[HashSize];

        // Validate before allocating the work buffer; never mutate caller-owned data.
        for (int i = 0; i < values.Length; i++)
            if (values[i] is null)
                throw new ArgumentException($"Element at index {i} is null.", nameof(values));

        // Hash each leaf directly into its slice of the work buffer.
        byte[] level = new byte[checked(values.Length * HashSize)];
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (int i = 0; i < values.Length; i++)
            HashLeaf(values[i], prefix, hasher, level.AsSpan(i * HashSize, HashSize));

        // Each parent overwrites already-consumed children. A fixed scratch buffer
        // prevents overlapping hash input/output and keeps branch allocation at zero.
        // Scratch layout: [prefix: 64 bytes | left hash: 32 bytes | right hash: 32 bytes].
        Span<byte> branch = stackalloc byte[128];
        prefix.CopyTo(branch);
        // Let H(message) = SHA256(prefix || message), and ab = H(a || b), etc.
        // Active levels: [a, b, c, d, e] -> [ab, cd, ee] -> [abcd, eeee] -> [root].
        // Counts: 5 -> 3 -> 2 -> 1; count / 2 + count % 2 rounds half upward.
        for (int count = values.Length; count > 1; count = count / 2 + count % 2)
        {
            for (int left = 0, parent = 0; left < count; left += 2, parent++)
            {
                // With count = 5, child indices are (0,1), (2,3), then (4,4).
                // The last pair reads e twice without appending another slot.
                // At count = 3, the same rule pairs the final subtree ee with itself.
                int right = Math.Min(left + 1, count - 1);
                level.AsSpan(left * HashSize, HashSize).CopyTo(branch[64..96]);
                level.AsSpan(right * HashSize, HashSize).CopyTo(branch[96..128]);
                // Copying both children above makes overwriting their old slots safe.
                // First round: [a,b,c,d,e] -> [ab,b,c,d,e] -> [ab,cd,c,d,e] -> [ab,cd,ee,d,e].
                // The next round uses only the first 3 slots; the trailing d and e are ignored.
                SHA256.HashData(branch, level.AsSpan(parent * HashSize, HashSize));
            }
        }
        return level.AsSpan(0, HashSize).ToArray();
    }

    private static void HashLeaf(string value, ReadOnlySpan<byte> prefix, IncrementalHash hasher, Span<byte> destination)
    {
        hasher.AppendData(prefix);
        // Bounded encoding storage, including for very large individual strings.
        var encoder = Utf8.GetEncoder();
        ReadOnlySpan<char> remaining = value.AsSpan();
        Span<byte> buffer = stackalloc byte[EncodingBufferSize];
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

    private static byte[] CreatePrefix(string tag)
    {
        // Untagged SHA-256 here is part of the BIP340 tag-prefix definition itself.
        byte[] tagHash = SHA256.HashData(Utf8.GetBytes(tag));
        byte[] prefix = new byte[64];
        tagHash.CopyTo(prefix, 0);
        tagHash.CopyTo(prefix, HashSize);
        return prefix;
    }
}

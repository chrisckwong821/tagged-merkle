namespace TaggedMerkle;

/// <summary>A sibling digest and its position relative to the node being proved.</summary>
/// <param name="Hash">Lowercase hexadecimal SHA-256 digest.</param>
/// <param name="Position">0 for a left sibling; 1 for a right sibling.</param>
public sealed record MerkleProofNode(string Hash, int Position);

/// <summary>Immutable retained tree levels, safe for concurrent proof generation.</summary>
public sealed class MerkleTreeSnapshot
{
    private readonly byte[][] levels;

    internal MerkleTreeSnapshot(byte[][] levels, int leafCount)
    {
        this.levels = levels;
        LeafCount = leafCount;
        RootHex = Convert.ToHexStringLower(leafCount == 0 ? new byte[MerkleTree.HashSize] : levels[^1]);
    }

    /// <summary>Number of original leaves, excluding duplicate-last padding.</summary>
    public int LeafCount { get; }

    /// <summary>Root in lowercase hexadecimal digest order.</summary>
    public string RootHex { get; }

    /// <summary>Returns siblings ordered from the leaf toward the root.</summary>
    public IReadOnlyList<MerkleProofNode> GetProof(int leafIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(leafIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(leafIndex, LeafCount);
        var proof = new MerkleProofNode[levels.Length - 1];
        for (int depth = 0; depth < proof.Length; depth++)
        {
            byte[] level = levels[depth];
            bool isRightChild = leafIndex % 2 == 1;
            int sibling = isRightChild ? leafIndex - 1 : Math.Min(leafIndex + 1, level.Length / MerkleTree.HashSize - 1);
            proof[depth] = new MerkleProofNode(
                Convert.ToHexStringLower(level.AsSpan(sibling * MerkleTree.HashSize, MerkleTree.HashSize)),
                isRightChild ? 0 : 1);
            leafIndex /= 2;
        }
        return Array.AsReadOnly(proof);
    }
}

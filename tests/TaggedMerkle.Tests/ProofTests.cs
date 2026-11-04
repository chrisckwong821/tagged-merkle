using System.Security.Cryptography;
using System.Text;
using TaggedMerkle;

internal static class ProofTests
{
    private const string LeafTag = "ProofOfReserve_Leaf";
    private const string BranchTag = "ProofOfReserve_Branch";

    public static IEnumerable<(string Category, string Name, Action Run)> Cases()
    {
        yield return Case("Eight-user known root", () =>
        {
            string[] values = Enumerable.Range(1, 8).Select(i => $"({i},{i * 1111})").ToArray();
            const string expected = "b1231de33da17c23cebd80c104b88198e0914b0463d0e14db163605b904a7ba3";
            Check(MerkleTree.CreateSnapshot(values, LeafTag, BranchTag).RootHex == expected);
            Check(Convert.ToHexStringLower(MerkleTree.ComputeRoot(values, LeafTag, BranchTag)) == expected);
        });
        yield return Case("Every proof across odd and even tree shapes", () =>
        {
            foreach (int count in Enumerable.Range(1, 70).Concat([127, 128, 129, 1000]))
            {
                string[] values = Enumerable.Range(0, count).Select(i => $"leaf-{i}").ToArray();
                var tree = MerkleTree.CreateSnapshot(values, LeafTag, BranchTag);
                Check(tree.RootHex == Convert.ToHexStringLower(MerkleTree.ComputeRoot(values, LeafTag, BranchTag)));
                for (int i = 0; i < count; i++)
                    Check(Verify(values[i], tree.GetProof(i)) == tree.RootHex);
            }
        });
        yield return Case("Empty and singleton snapshots", () =>
        {
            var empty = MerkleTree.CreateSnapshot([], LeafTag, BranchTag);
            Check(empty.LeafCount == 0 && empty.RootHex == new string('0', 64));
            Throws<ArgumentOutOfRangeException>(() => empty.GetProof(0));
            var single = MerkleTree.CreateSnapshot(["(10,100)"], LeafTag, BranchTag);
            Check(single.GetProof(0).Count == 0);
            Check(single.RootHex == Verify("(10,100)", []));
        });
        yield return Case("Snapshot input validation", () =>
        {
            Throws<ArgumentNullException>(() => MerkleTree.CreateSnapshot(null!, LeafTag, BranchTag));
            Throws<ArgumentNullException>(() => MerkleTree.CreateSnapshot([], null!, BranchTag));
            Throws<ArgumentNullException>(() => MerkleTree.ComputeRoot([], LeafTag, null!));
            Throws<ArgumentException>(() => MerkleTree.CreateSnapshot(["ok", null!], LeafTag, BranchTag));
            Throws<EncoderFallbackException>(() => MerkleTree.CreateSnapshot(["\ud800"], LeafTag, BranchTag));
            Throws<EncoderFallbackException>(() => MerkleTree.CreateSnapshot([], LeafTag, "\ud800"));
        });
        yield return Case("Invalid proof indexes", () =>
        {
            var tree = MerkleTree.CreateSnapshot(["a", "b"], LeafTag, BranchTag);
            Throws<ArgumentOutOfRangeException>(() => tree.GetProof(-1));
            Throws<ArgumentOutOfRangeException>(() => tree.GetProof(2));
        });
        yield return Case("Snapshot remains stable after caller changes input", () =>
        {
            string[] input = ["a", "b", "c"];
            var tree = MerkleTree.CreateSnapshot(input, LeafTag, BranchTag);
            input[0] = "changed";
            Check(Verify("a", tree.GetProof(0)) == tree.RootHex);
            Parallel.For(0, 100, _ => Check(Verify("c", tree.GetProof(2)) == tree.RootHex));
        });
        yield return Case("Same-tag overload preserves existing contract", () =>
        {
            string[] values = ["aaa", "bbb", "ccc", "ddd", "eee"];
            var tree = MerkleTree.CreateSnapshot(values, "Bitcoin_Transaction", "Bitcoin_Transaction");
            Check(tree.RootHex == Convert.ToHexStringLower(MerkleTree.ComputeRoot(values, "Bitcoin_Transaction")));
        });
        yield return Case("Changed balance or sibling position invalidates proof", () =>
        {
            var tree = MerkleTree.CreateSnapshot(["(1,1111)", "(2,2222)"], LeafTag, BranchTag);
            var proof = tree.GetProof(0);
            Check(Verify("(1,9999)", proof) != tree.RootHex);
            Check(Verify("(1,1111)", [proof[0] with { Position = 0 }]) != tree.RootHex);
        });
    }

    private static (string, string, Action) Case(string name, Action run) => ("Merkle proofs", name, run);

    private static string Verify(string value, IReadOnlyList<MerkleProofNode> proof)
    {
        byte[] Hash(string tag, byte[] message)
        {
            byte[] prefix = SHA256.HashData(Encoding.UTF8.GetBytes(tag));
            return SHA256.HashData([.. prefix, .. prefix, .. message]);
        }
        byte[] current = Hash(LeafTag, Encoding.UTF8.GetBytes(value));
        foreach (var node in proof)
        {
            Check(node.Position is 0 or 1);
            byte[] sibling = Convert.FromHexString(node.Hash);
            current = Hash(BranchTag, node.Position == 0 ? [.. sibling, .. current] : [.. current, .. sibling]);
        }
        return Convert.ToHexStringLower(current);
    }

    private static void Check(bool condition)
    {
        if (!condition) throw new Exception("Proof assertion failed.");
    }

    private static void Throws<T>(Action run) where T : Exception
    {
        try { run(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}

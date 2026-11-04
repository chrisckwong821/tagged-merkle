# Tagged Merkle

[![build-and-test](https://github.com/chrisckwong821/tagged-merkle/actions/workflows/ci.yml/badge.svg)](https://github.com/chrisckwong821/tagged-merkle/actions/workflows/ci.yml)

A small `C# / .NET 10` library that turns an ordered string array into a 32-byte Merkle root and produces inclusion proofs. It uses Bitcoin's tree shape with [BIP340](https://github.com/bitcoin/bips/blob/master/bip-0340.mediawiki) tagged SHA-256 hashing, and has no third-party dependencies.

## Install

The library is not on NuGet. Add the repository as a Git submodule pinned to a release, then reference the project:

```sh
git submodule add https://github.com/chrisckwong821/tagged-merkle.git external/tagged-merkle
git -C external/tagged-merkle checkout v0.1.0
dotnet add reference external/tagged-merkle/src/TaggedMerkle/TaggedMerkle.csproj
```

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). See [Releases](https://github.com/chrisckwong821/tagged-merkle/releases) for versions and changes.

## Quick start

### Compute a root

```csharp
using TaggedMerkle;

byte[] root = MerkleTree.ComputeRoot(["aaa", "bbb", "ccc", "ddd", "eee"], "Bitcoin_Transaction");
Console.WriteLine(Convert.ToHexStringLower(root));
// 4aa906745f72053498ecc74f79813370a4fe04f85e09421df2d5ef760dfa94b5
```

Pass two tags to hash leaves and branches differently (recommended, see [Security notes](#security-notes-and-limitations)):

```csharp
byte[] root = MerkleTree.ComputeRoot(values, "ProofOfReserve_Leaf", "ProofOfReserve_Branch");
```

### Create proofs

A snapshot keeps every tree level, so you can produce many proofs without rebuilding the tree:

```csharp
MerkleTreeSnapshot tree = MerkleTree.CreateSnapshot(
    ["(1,1111)", "(2,2222)", "(3,3333)"],
    "ProofOfReserve_Leaf",
    "ProofOfReserve_Branch");

string root = tree.RootHex;                          // publish this
IReadOnlyList<MerkleProofNode> proof = tree.GetProof(0); // give this to the owner of leaf 0
```

Each `MerkleProofNode` has a lowercase hex `Hash` and a `Position`: `0` if the sibling is on the left, `1` if it is on the right. Nodes run from the leaf toward the root. A single-leaf tree has an empty proof.

## API

| Member | Description |
| --- | --- |
| `MerkleTree.ComputeRoot(values, tag)` | Root as a new 32-byte array; one tag for leaves and branches. |
| `MerkleTree.ComputeRoot(values, leafTag, branchTag)` | Root with separate leaf and branch tags. |
| `MerkleTree.CreateSnapshot(values, leafTag, branchTag)` | Immutable tree that keeps its levels for proofs. |
| `MerkleTreeSnapshot.RootHex` | Root as lowercase hex. |
| `MerkleTreeSnapshot.LeafCount` | Number of input values. |
| `MerkleTreeSnapshot.GetProof(leafIndex)` | Sibling hashes from leaf to root, in O(log n). |
| `MerkleProofNode(Hash, Position)` | One proof step: sibling hash and side (`0` left, `1` right). |

**Errors:**
- **`ArgumentNullException`:** a null array or tag.
- **`ArgumentException`:** a null element; the message gives its index.
- **`EncoderFallbackException`:** invalid UTF-16 in a value or tag, such as an unpaired surrogate.
- **`ArgumentOutOfRangeException`:** a `GetProof` index outside `0..LeafCount-1`.

**Thread safety:** `ComputeRoot` and `CreateSnapshot` never modify the input array, but it must not change while they run. A snapshot is immutable and safe for concurrent `GetProof` calls.

## Hashing specification

| Item | Rule |
| --- | --- |
| Text | Values and tags are encoded as strict UTF-8, without BOM, trimming, or Unicode normalization. Empty strings and empty tags are valid. |
| Tree | Keep input order and pair adjacent nodes. At each odd-sized level, pair the last node with itself, until one node remains. |
| Output | 32 bytes in SHA-256 digest order, without Bitcoin's display byte reversal. |
| Edge cases | One value: the root is its leaf hash. No values: the root is 32 zero bytes. |

Here `||` joins raw bytes:

```text
TaggedHash(tag, message) = SHA256(SHA256(UTF8(tag)) || SHA256(UTF8(tag)) || message)

leaf   = TaggedHash(leafTag,   UTF8(value))
parent = TaggedHash(branchTag, leftHash || rightHash)
```

With the two-argument `ComputeRoot`, `leafTag` and `branchTag` are the same.

## Security notes and limitations

- **Use different leaf and branch tags.** With one shared tag, a value whose 64 UTF-8 bytes equal two child hashes gets the same hash as that branch, so leaves and internal nodes can be confused. Different tags rule this out.
- **Duplicate tails:** different lists can have the same root without a SHA-256 collision, because the last node is paired with itself. For example, `[a, b, c]` and `[a, b, c, c]` have the same root. If the leaf count or list structure matters, commit to it separately or encode it in the values.
- **No normalization:** `"é"` and `"é"` look alike but produce different roots. Normalize values yourself if needed.

## Performance

`n` is the number of values, `B` their total UTF-8 bytes, and `t` the tag length.

| | Cost |
| --- | --- |
| `ComputeRoot` time | `O(B + n + t)` |
| `ComputeRoot` extra memory | One `32 × n` byte buffer (about 3 MiB for 100,000 values) plus 4 KiB of reused scratch |
| `CreateSnapshot` memory kept | About `64 × n` bytes for all levels |
| `GetProof` | `O(log n)` time and output |

Benchmark of `ComputeRoot` on an Apple M4 Max, .NET 10.0.14, Release build (2026-11-04). Times are the median of seven calls after three warmups:

| Values | Bytes per value | Median | Payload MiB/s | Managed MiB allocated |
| ---: | ---: | ---: | ---: | ---: |
| 1,000 | 32 | 0.30 ms | 102 | 0.03 |
| 100,000 | 32 | 28.35 ms | 108 | 3.05 |
| 1,000,000 | 32 | 248.85 ms | 123 | 30.52 |
| 100,000 | 20 | 25.20 ms | 76 | 3.05 |
| 1,000 | 4,096 | 1.52 ms | 2,566 | 0.03 |

These are single-machine measurements, not guarantees. The work is CPU-bound, so async wrappers will not speed it up. Reproduce with:

```sh
dotnet run --project benchmarks/TaggedMerkle.Benchmarks -c Release
```

## Build and test

```sh
dotnet build -c Release
dotnet run --project tests/TaggedMerkle.Tests -c Release --no-build
```

The test runner is a console app, not a `dotnet test` project. It runs 39 checks: known answers, input validation, UTF-8 encoding, tree shapes, randomized comparison with a reference model, tags, and proofs. [docs/TESTING.md](docs/TESTING.md) describes every check. CI runs the same commands on Linux, Windows, and macOS.

## License

[MIT](LICENSE)

## References

- [BIP340 tagged hashing](https://github.com/bitcoin/bips/blob/master/bip-0340.mediawiki)
- [Bitcoin Core Merkle algorithm](https://github.com/bitcoin/bitcoin/blob/master/src/consensus/merkle.cpp)

## Appendix: Bitcoin tree shape

With five leaves, `e` is paired with itself at the bottom, then the `ee` subtree is paired with itself at the next level:

```text
                    root
            /                  \
        H(ab,cd)              H(ee,ee)       ← 3 nodes: pair ee with itself
       /       \               /     \
    H(a,b)   H(c,d)        H(e,e)   H(e,e)
    /  \     /  \           /  \
   a    b   c    d         e    e            ← 5 leaves: pair e with itself
```

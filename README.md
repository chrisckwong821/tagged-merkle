# Tagged Merkle

- A `C# / .NET 10` library that turns an ordered string array into a `32-byte` Merkle root.
- Uses Bitcoin's tree shape and `BIP340` tagged hashing with a caller-supplied tag.
- Requires no third-party libraries or NuGet packages.

## Use

- Reference `src/TaggedMerkle/TaggedMerkle.csproj` from your project.
- Use `ComputeRoot` for raw bytes and `Convert.ToHexStringLower` for display.

```csharp
using TaggedMerkle;

string tag = "Bitcoin_Transaction"; // Required by Q1
byte[] root = MerkleTree.ComputeRoot(["aaa", "bbb", "ccc", "ddd", "eee"], tag);
Console.WriteLine(Convert.ToHexStringLower(root));
// 4aa906745f72053498ecc74f79813370a4fe04f85e09421df2d5ef760dfa94b5
```

## Build and verify

- Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Run the executable test suite with `dotnet run`; it is not a `dotnet test` project.

```sh
dotnet build -c Release
dotnet run --project tests/TaggedMerkle.Tests -c Release --no-build
```

## Inputs, outputs, and rules

| Item | Behavior |
| --- | --- |
| Input | `string[] values` and `string tag`; use `Bitcoin_Transaction` for Q1 |
| Output | A new `32-byte` root in digest order, without byte reversal |
| Text | Encode strings and tag as strict `UTF-8`, without BOM, trimming, or normalization; empty strings and tags are valid |
| Tree | Keep input order, pair adjacent nodes, and duplicate the last node at each odd-sized level until one remains |
| Edge cases | One string returns its leaf hash; an empty array returns 32 zero bytes |
| Validation | Reject null arrays, elements, or tags, and invalid UTF-16 |
| Ownership | Input is unchanged; do not modify it during calculation |

Both leaves and parents use the supplied tag. Here `||` joins raw bytes:

```text
T = SHA256(UTF8(tag))
P = T || T
H(message) = SHA256(P || message)

leaf   = H(UTF8(string))
parent = H(leftHash || rightHash)
```

###  Limitations

- **Duplicate tails:** different input lists can have the same root without a SHA-256 collision.

  ```text
  [a, b, c] and [a, b, c, c] → same root
  ```

- **Shared tag:** each calculation uses the same supplied tag for leaves and branches, so it does not distinguish those roles.
- **Application commitments:** applications needing an unambiguous commitment must authenticate list count and structure separately or choose a different construction.


## Test coverage

- The runner prints `31 named checks`, grouped by category, with a pass/fail summary.
- Several checks cover multiple inputs; this is a scenario count, not a measured code-coverage percentage.

- **Q1 inputs:** checks in categories 1–5 pass `"Bitcoin_Transaction"` explicitly.

### 1. Known answers — 11 checks

- **Required root:** compare the result with the fixed expected root in the usage example.

  ```csharp
  ["aaa", "bbb", "ccc", "ddd", "eee"]
  ```

- **Singleton:** match the known leaf hash without an extra branch hash.

  ```csharp
  ["aaa"]
  ```

- **Empty string:** match its known leaf hash, which differs from an empty tree's zero result.

  ```csharp
  [""]
  ```

- **Eight independent vectors:** compare each input with a fixed, independently verified root.

  ```csharp
  ["item0"]
  ["item0", "item1"]
  ["item0", "item1", "item2"]
  ["item0", "item1", "item2", "item3"]
  ["item0", "item1", "item2", "item3", "item4", "item5"]
  ["item0", "item1", "item2", "item3", "item4", "item5", "item6"]
  ["item0", "item1", "item2", "item3", "item4", "item5", "item6", "item7"]
  ["item0", "item1", "item2", "item3", "item4", "item5", "item6", "item7", "item8"]
  ```

### 2. Input validation — 4 checks

- **Empty tree:** return `32` zero bytes.

  ```csharp
  []
  ```

- **Null array:** throw `ArgumentNullException`.

  ```csharp
  null
  ```

- **Null element:** throw `ArgumentException` naming the `values` parameter and the correct element index.

  ```csharp
  [null, "b", "c"]  // Index 0
  ["a", null, "c"]  // Index 1
  ["a", "b", null]  // Index 2
  ```

- **Invalid Unicode:** throw `EncoderFallbackException` for an unpaired surrogate.

  ```csharp
  ["\uD800"]                         // High surrogate without its pair
  ["\uDC00"]                         // Low surrogate without its pair
  [new string('x', 4095) + "\uD800"]  // Invalid character after a long prefix
  ```

### 3. Text encoding — 4 checks

- **Encoding:** `SHA-256` hashes bytes, so strings are converted to `UTF-8` first.
- **Leaves:** each array element becomes one leaf, regardless of its number of characters.

```text
["日本語"]          → one leaf   → H(all 9 UTF-8 bytes)
["日", "本", "語"]  → three leaves → hash separately, then build the tree
```

#### Check 1: UTF-8 and encoding boundaries

- **Expected behavior:** match an independent calculation that encodes each complete string at once.
- **Notation:** hexadecimal pairs below represent actual bytes; message lengths exclude the `64-byte` tag prefix.

| Input | UTF-8 message | Expected root |
| --- | --- | --- |
| `["日本語"]` | `E6 97 A5 E6 9C AC E8 AA 9E` — 9 bytes | `H(those 9 bytes)` |
| `["🙂"]` | `F0 9F 99 82` — 4 bytes | `H(those 4 bytes)` |
| `["a\0b"]` | `61 00 62` — 3 bytes | `H(61 00 62)` |
| `[""]` | Zero message bytes | `SHA256(P)` |
| `[new string('x', 4095) + "🙂"]` | 4,095 copies of `78`, then `F0 9F 99 82` | `H(all 4,099 bytes)` |
| `[new string('界', 10_000)]` | 10,000 copies of `E7 95 8C` | `H(all 30,000 bytes)` |

- **Repeated characters:** `new string('界', 10_000)` repeats `界` 10,000 times; each copy uses three UTF-8 bytes.

```text
10,000 × 3 bytes                 = 30,000 message bytes
64-byte prefix + 30,000 bytes    = 30,064 bytes fed to SHA-256
Final digest                    = 32 bytes
```

- **Small buffer:** the library reuses a `4,096-byte` buffer instead of allocating the entire encoded string.
- **Continuous hashing:** the hasher remembers its progress between chunks and finishes once per leaf.

```text
Reference: SHA256(P || complete message)

Library:   Append(P)
           Encode next portion → Append(bytes) → reuse buffer
           Repeat until the string ends
           Finish once

Result:    identical digest
```

- **Character boundaries:** a character that does not fit waits for the next chunk; chunks need not fill the buffer.
- **Tree boundaries:** encoding chunks remain parts of one leaf, not additional leaves or separately hashed messages.

```text
Input: 4,095 copies of "x", followed by "🙂"

Chunk 1: 4,095 bytes for the x characters
Chunk 2:     4 bytes for the emoji
Result: H(all 4,099 bytes in order)
```

#### Check 2: No implicit Unicode normalization

- **Expected behavior:** the following roots differ because their UTF-8 bytes differ.
- **Assertion:** this check compares the two roots rather than storing a fixed digest for each.

```text
["é"]       → C3 A9    → H(C3 A9)
["e\u0301"] → 65 CC 81 → H(65 CC 81)

They can look alike, but the second input is e + combining accent.
```

#### Check 3: Chunk boundaries followed by another leaf

- **Input:** test each listed length with a long first string and a short second string.

```csharp
// n = 4095, 4096, 4097, 8191, 8192, 8193, or 100000
[new string('x', n) + "🙂末尾", "next leaf"]
```

- **Expected hashing:** calculate the two leaf hashes separately, then hash their raw digests together.

```text
firstMessage  = n copies of 78, then F0 9F 99 82 E6 9C AB E5 B0 BE
secondMessage = 6E 65 78 74 20 6C 65 61 66

left  = H(firstMessage)   // n + 10 message bytes
right = H(secondMessage)  // 9 message bytes; fresh hash state
root  = H(left || right)  // 64 message bytes + 64-byte tag prefix
```

- **Verification:** compare with the independent reference model to detect missing bytes or leftover hash state from the first leaf.
- **Tree shape:** there are exactly two leaves, so no odd-node duplication is needed.

#### Check 4: Whitespace and null characters

- **Expected behavior:** preserve every character and match the reference calculation.
- **Comparison:** each result must differ from the baseline `["a"]`, whose root is `H(61)`.

```text
[" a"]  → 20 61 → H(20 61)  // Leading space
["a "]  → 61 20 → H(61 20)  // Trailing space
["a\n"] → 61 0A → H(61 0A)  // Newline
["a\0"] → 61 00 → H(61 00)  // Null character
```

- **Escapes:** `\n`, `\0`, and `\u0301` represent actual characters in C# strings.
- **Null distinction:** `"\0"` is a valid string containing byte `00` after encoding; a `null` array element is rejected.

### 4. Tree structure — 4 checks

- **Ordering:** reversing the input changes the root.

  ```csharp
  ["a", "b"]
  ["b", "a"]  // Different root
  ```

- **Odd levels:** compare `83` tree sizes against the reference model, including sizes around powers of two.

  ```text
  Pattern: ["leaf-0", "leaf-1", ..., "leaf-{n-1}"]
  Example: ["leaf-0", "leaf-1", "leaf-2"]

  n = 1 through 70, then:
      127, 128, 129, 255, 256, 257, 1000,
      1023, 1024, 1025, 4095, 4096, 4097
  ```

- **Duplicate-tail ambiguity:** Bitcoin's required duplication rule makes these roots equal.

  ```csharp
  ["a", "b", "c"]
  ["a", "b", "c", "c"]  // Same root
  ```

- **Explicit five-leaf tree:** independently assemble each branch and compare with both the fixed answer and library output.

  ```text
  Input: ["aaa", "bbb", "ccc", "ddd", "eee"]
  Leaves: a, b, c, d, e
  ab = H(a || b); cd = H(c || d); ee = H(e || e)
  root = H(H(ab || cd) || H(ee || ee))
  ```

### 5. Scale and differential checks — 2 checks

- **Randomized comparison:** compare `150` generated arrays with the independent reference model using seed `340`.
- **Generation:** join complete fragments to preserve valid emoji; the recipe permits empty arrays and empty strings.

```csharp
var random = new Random(340);
string[] fragments = ["a", "z", "0", " ", "é", "e\u0301", "日", "界", "🙂", "\0"];
// Each array: 0–299 strings
// Each string: 0–63 randomly selected fragments joined together
```

- **Large input:** compare a tree of exactly `100,001` identical strings with the reference model.

```csharp
Enumerable.Repeat("transaction", 100_001).ToArray()
```

### 6. Tag selection — 6 checks

- **Known answer:** the empty-string leaf with tag `"A"` matches an independently verified digest.

  ```csharp
  MerkleTree.ComputeRoot([""], "A")
  // e2d7d313d2e64f38e362097ddc2751bd68731aee8a851fa94e7b6a4b327ff5ab
  ```

- **Custom tags:** compare the full tree with an independent reference for every tag and size below.

  ```text
  Tags: "", "A", "B", "日本語🙂", or 4,097 copies of "x"
  Input: ["leaf-0", ..., "leaf-{n-1}"]
  n: 1, 2, 3, 5, 9, 33
  ```

- **Exact tag bytes:** different encodings produce different roots for the same input; repeating `"A"` produces its original root again.

  ```text
  Input: ["aaa", "bbb", "ccc"]
  Tags, in call order: "A", "a", " A", "A ", "é", "e\u0301", "A"
  ```

- **Null tags:** reject `null` with `ArgumentNullException`, whether the input is `[]` or `["aaa"]`.
- **Invalid Unicode tags:** reject `"\uD800"` and `"\uDC00"` with `EncoderFallbackException` for both `[]` and `["aaa"]`.
- **Empty-tree sentinel:** return 32 zero bytes for `[]` with any valid tag, tested with `""`, `"A"`, `"Bitcoin_Transaction"`, and `"日本語🙂"`.

## Performance and complexity

### Cost as input grows

- `n` = number of strings, `B` = their total UTF-8 bytes, and `t` = tag UTF-8 bytes.

| Cost | Complexity / size |
| --- | --- |
| Time | `O(B + n + t)` for encoding, tag hashing, and tree reduction |
| Additional working memory | `O(n + t)`, excluding caller-owned input strings |
| Main hash buffer | `32 × n` bytes: about `3.05 MiB` for 100,000 leaves |
| Encoding scratch space | `4 KiB`, reused for each string |
| Branch scratch space | `128 bytes`, reused for each parent |

- **CPU-bound:** the calculation does no file or network I/O; its work is encoding and SHA-256 hashing.
- **Memory costs:** large inputs also incur allocation, garbage-collection, and memory-bandwidth costs; each leaf creates an encoder object.
- **Design:** the function leaves inputs unchanged, while local mutation reuses child-hash storage for parents and computes the tag prefix once per call.
- **Scaling:** limit input sizes and simultaneous calls to available memory and CPU; async wrappers alone will not speed up this calculation.

### Basic local benchmark

- **Environment:** Apple M4 Max, 64 GiB RAM, macOS 15.3.2, ARM64, .NET 10.0.14, Release build; measured on 2026-11-03.
- **Method:** sequential calls with `Bitcoin_Transaction`, three warmups and seven measured calls per workload; inputs are unique ASCII strings of the stated UTF-8 length.
- **Timing:** input creation and console output are excluded; garbage collection is forced before each sample, outside the timer.

| Strings | Bytes per string | Median per root | Min–max | Payload MiB/s | Managed MiB allocated per root |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1,000 | 32 | 0.43 ms | 0.42–0.45 ms | 70.41 | 0.08 |
| 100,000 | 32 | 32.87 ms | 32.01–35.97 ms | 92.85 | 7.63 |
| 1,000,000 | 32 | 321.76 ms | 316.42–327.66 ms | 94.84 | 76.29 |
| 100,000 | 20 | 31.85 ms | 31.57–32.97 ms | 59.89 | 7.63 |
| 1,000 | 4,096 | 1.61 ms | 1.59–1.67 ms | 2432.74 | 0.08 |

- **Throughput:** `Payload MiB/s` means input bytes divided by root-calculation time, including tree work; it is not a disk/network rate.
- **Allocations:** cumulative managed allocations include the hash buffer and temporary objects, but exclude input creation; they are not peak memory, stack/native memory, or process RAM usage.
- **Interpretation:** short strings incur more per-leaf work per payload byte; longer strings can therefore show higher payload throughput.
- **Limits:** these are basic local measurements, not a service-load benchmark; small timing differences, such as the 20-byte versus 32-byte cases, should not be treated as guaranteed speedups.

Reproduce with the dependency-free [benchmark runner](benchmarks/TaggedMerkle.Benchmarks/Program.cs):

```sh
dotnet run --project benchmarks/TaggedMerkle.Benchmarks -c Release
```

## Dependencies

- **Library, tests, and benchmark:** no third-party libraries or NuGet packages.
- **CI:** GitHub's `actions/checkout` fetches the repository and `actions/setup-dotnet` installs the SDK.

| Built-in namespace | Purpose |
| --- | --- |
| `System.Security.Cryptography` | SHA-256 and incremental hashing |
| `System.Text` | Strict UTF-8 encoding |
| `System.Diagnostics` — benchmark | High-resolution elapsed-time measurement |
| `System.Linq` — tests and benchmark | Reference reduction and benchmark input generation |

## References

- [BIP340 tagged hashing](https://github.com/bitcoin/bips/blob/master/bip-0340.mediawiki)
- [Bitcoin Core Merkle algorithm](https://github.com/bitcoin/bitcoin/blob/master/src/consensus/merkle.cpp)

## Release

- The initial release uses the Git tag `v0.0.1`; commit the final changes and ensure the tag points to that commit before publishing.
- Replace `OWNER` with your personal GitHub account. These commands publish the public repository and tag without using a package registry.

```sh
gh repo create OWNER/tagged-merkle --public --source=. --remote=origin --push
git push origin v0.0.1
```

## Appendix: Bitcoin tree shape

1. **Leaves:** keep transaction hashes (`txid`s) in block order.
2. **Parents:** pair adjacent nodes from left to right and hash their concatenated bytes.
3. **Odd levels:** pair the final node with itself at every odd-sized level.
4. **Root:** repeat until one node remains.
5. **Singleton:** one transaction's hash is already the root.

- **Five-leaf example:** `e` is duplicated at the bottom, then its `ee` subtree is duplicated at the next level.
- **Notation:** `H(left, right)` below means hashing the concatenated child hashes; this library uses the tagged hash defined above.

```text
                    root
            /                  \
        H(ab,cd)              H(ee,ee)       ← Duplicate ee at the 3-node level
       /       \               /     \
    H(a,b)   H(c,d)        H(e,e)   H(e,e)
    /  \     /  \           /  \
   a    b   c    d         e    e            ← Duplicate e at the 5-leaf level
```

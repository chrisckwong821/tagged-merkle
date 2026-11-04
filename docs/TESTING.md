# Test coverage

The test runner in [`tests/TaggedMerkle.Tests`](../tests/TaggedMerkle.Tests) is a console app, not a `dotnet test` project. It prints **39 named checks** in seven categories, with a pass/fail summary:

```sh
dotnet build -c Release
dotnet run --project tests/TaggedMerkle.Tests -c Release --no-build
```

- Several checks cover many inputs, so 39 counts scenarios; it is not a code-coverage percentage.
- Checks in categories 1–5 use the tag `"Bitcoin_Transaction"`.
- `H(message)` below is the tagged hash defined in the [README](../README.md#hashing-specification): `SHA256(P || message)`, where `P` is the 64-byte tag prefix.

## 1. Known answers — 11 checks

- **Required root:** compare the result with the fixed root `4aa90674…94b5` from the README quick start.

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

## 2. Input validation — 4 checks

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

## 3. Text encoding — 4 checks

- **Encoding:** `SHA-256` hashes bytes, so strings are converted to `UTF-8` first.
- **Leaves:** each array element becomes one leaf, regardless of its number of characters.

```text
["日本語"]          → one leaf   → H(all 9 UTF-8 bytes)
["日", "本", "語"]  → three leaves → hash separately, then build the tree
```

### Check 1: UTF-8 and encoding boundaries

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

### Check 2: No implicit Unicode normalization

- **Expected behavior:** the following roots differ because their UTF-8 bytes differ.
- **Assertion:** this check compares the two roots rather than storing a fixed digest for each.

```text
["é"]       → C3 A9    → H(C3 A9)
["e\u0301"] → 65 CC 81 → H(65 CC 81)

They can look alike, but the second input is e + combining accent.
```

### Check 3: Chunk boundaries followed by another leaf

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

### Check 4: Whitespace and null characters

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

## 4. Tree structure — 4 checks

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

## 5. Scale and differential checks — 2 checks

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

## 6. Tag selection — 6 checks

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

## 7. Merkle proofs — 8 checks

These checks are in [`ProofTests.cs`](../tests/TaggedMerkle.Tests/ProofTests.cs). They use the tags `ProofOfReserve_Leaf` and `ProofOfReserve_Branch` unless stated otherwise, and an independent verifier that rebuilds the root from a value and its proof.

- **Eight-user known root:** both `CreateSnapshot` and `ComputeRoot` return `b1231de33da17c23cebd80c104b88198e0914b0463d0e14db163605b904a7ba3`.

  ```csharp
  ["(1,1111)", "(2,2222)", ..., "(8,8888)"]
  ```

- **Every proof across tree shapes:** for 1–70, 127, 128, 129 and 1,000 leaves, the snapshot root equals `ComputeRoot`, and every leaf's proof verifies.
- **Empty and singleton snapshots:** an empty snapshot has `LeafCount == 0`, a zero root, and rejects `GetProof(0)`; a singleton's proof is empty and its root is the leaf hash.
- **Input validation:** null arrays, tags, and elements throw `ArgumentNullException` or `ArgumentException`; invalid UTF-16 in a value or tag throws `EncoderFallbackException`, even for an empty tree.
- **Invalid proof indexes:** `GetProof(-1)` and `GetProof(LeafCount)` throw `ArgumentOutOfRangeException`.
- **Snapshot stability:** changing the caller's array after `CreateSnapshot` does not change proofs, and 100 parallel `GetProof` calls all verify.
- **Same-tag overload:** `CreateSnapshot(values, tag, tag)` matches the two-argument `ComputeRoot(values, tag)`.
- **Tampering:** a changed value, or a sibling with the wrong `Position`, no longer verifies against the root.

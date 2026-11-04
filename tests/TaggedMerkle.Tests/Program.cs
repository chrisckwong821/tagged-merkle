using System.Security.Cryptography;
using System.Text;
using TaggedMerkle;

// Dependency-free executable test suite: any failed assertion exits nonzero.
const string ExampleTag = "Bitcoin_Transaction";
const string ExampleRoot = "4aa906745f72053498ecc74f79813370a4fe04f85e09421df2d5ef760dfa94b5";
string[] example = ["aaa", "bbb", "ccc", "ddd", "eee"];

// Register tests; their actions run later in the reporting loop.
List<(string Category, string Name, Action Run)> tests =
[
    ("Known answers", "Required five-leaf known answer", () =>
        Equal(
            ExampleRoot,
            Convert.ToHexStringLower(MerkleTree.ComputeRoot(example, ExampleTag)))),

    ("Known answers", "Singleton known answer", () =>
        Equal(
            "d2d838724571ff750eb7f498a667c32f522efae2b403eae6f678207ac6f978de",
            Convert.ToHexStringLower(MerkleTree.ComputeRoot(["aaa"], ExampleTag)))),

    ("Known answers", "Empty string is a leaf", () =>
        Equal(
            "7288e9650f39079335493343efab78b07d5ba3883424bc5428b797450b7c4aac",
            Convert.ToHexStringLower(MerkleTree.ComputeRoot([""], ExampleTag)))),

    ("Input validation", "Empty tree is zero", () =>
        Equal(
            new string('0', 64),
            Convert.ToHexStringLower(MerkleTree.ComputeRoot([], ExampleTag)))),

    ("Input validation", "Null array rejected", () =>
        Throws<ArgumentNullException>(() => MerkleTree.ComputeRoot(null!, ExampleTag))),

    ("Input validation", "Null elements report their position", () =>
    {
        for (int index = 0; index < 3; index++)
        {
            string[] values = ["a", "b", "c"];
            values[index] = null!;

            var error = Throws<ArgumentException>(
                () => MerkleTree.ComputeRoot(values, ExampleTag));

            Equal("values", error.ParamName!);
            if (!error.Message.Contains($"index {index}", StringComparison.Ordinal))
            {
                throw new Exception($"Missing null element index {index}.");
            }
        }
    }),

    ("Input validation", "Invalid UTF-16 rejected", () =>
    {
        foreach (string value in new[]
        {
            "\ud800",
            "\udc00",
            new string('x', 4095) + "\ud800"
        })
        {
            Throws<EncoderFallbackException>(() => MerkleTree.ComputeRoot([value], ExampleTag));
        }
    }),

    ("Text encoding", "UTF-8 and encoding boundaries", () =>
    {
        foreach (string value in new[]
        {
            "日本語",
            "🙂",
            "a\0b",
            "",
            new string('x', 4095) + "🙂",
            new string('界', 10_000)
        })
        {
            Equal(
                Reference([value]),
                Convert.ToHexStringLower(MerkleTree.ComputeRoot([value], ExampleTag)));
        }
    }),

    ("Text encoding", "No implicit Unicode normalization", () =>
        Different(
            Convert.ToHexStringLower(MerkleTree.ComputeRoot(["é"], ExampleTag)),
            Convert.ToHexStringLower(MerkleTree.ComputeRoot(["e\u0301"], ExampleTag)))),

    ("Tree structure", "Ordering matters", () =>
        Different(
            Convert.ToHexStringLower(MerkleTree.ComputeRoot(["a", "b"], ExampleTag)),
            Convert.ToHexStringLower(MerkleTree.ComputeRoot(["b", "a"], ExampleTag)))),

    ("Tree structure", "Odd duplication is applied at every level", () =>
    {
        foreach (int count in Enumerable.Range(1, 70).Concat(
        [
            127, 128, 129,
            255, 256, 257,
            1000, 1023, 1024, 1025,
            4095, 4096, 4097
        ]))
        {
            string[] values = Enumerable.Range(0, count)
                .Select(i => $"leaf-{i}")
                .ToArray();

            Equal(
                Reference(values),
                Convert.ToHexStringLower(MerkleTree.ComputeRoot(values, ExampleTag)),
                $"{values.Length} leaves");
        }
    }),

    ("Tree structure", "Bitcoin duplicate-tail ambiguity is preserved", () =>
        Equal(
            Convert.ToHexStringLower(MerkleTree.ComputeRoot(["a", "b", "c"], ExampleTag)),
            Convert.ToHexStringLower(MerkleTree.ComputeRoot(["a", "b", "c", "c"], ExampleTag)))),

    ("Scale and differential checks", "Deterministic randomized differential tests", () =>
    {
        var random = new Random(340);
        for (int iteration = 0; iteration < 150; iteration++)
        {
            string[] values = Enumerable.Range(0, random.Next(0, 300))
                .Select(_ => RandomText(random))
                .ToArray();

            Equal(
                Reference(values),
                Convert.ToHexStringLower(MerkleTree.ComputeRoot(values, ExampleTag)),
                $"{values.Length} leaves");
        }
    }),

    ("Scale and differential checks", "Practical large input", () =>
    {
        string[] values = Enumerable.Repeat("transaction", 100_001).ToArray();

        Equal(
            Reference(values),
            Convert.ToHexStringLower(MerkleTree.ComputeRoot(values, ExampleTag)),
            $"{values.Length} leaves");
    })
];

// Known-answer cases also present in the merkle-root reference project.
// Each literal was independently rechecked with Python hashlib during this change.
(int Count, string Expected)[] vectors =
[
    (1, "edec914c25477fa4022c7782dd9f228ee0c8bab1513d6354fdab98501d992134"),
    (2, "71f7b716f815f1ac95d28c427db6c28b66f3d3fc5ac745eee5426bfd6508b14b"),
    (3, "3556740716fa2dd4eee7717255d16cf16c804b97bb21e073adf20defc3b8e011"),
    (4, "f84ce02dea69e04c9be4a322757af6c1cb629954ba704e50bad3c3c8cd1c52b7"),
    (6, "37aa372bcf8f17bb4b93f8fe4df81947187ac53dbd0cb3002a960b86854f2ada"),
    (7, "7fb21805d99860e9497c72d3a2d98cb908b5041d8e8508b36baaccb60dbd5c1a"),
    (8, "ba76acbe81e1c128d05fea48624def3b1b66e977e8e91a1d1972ea2e952b711e"),
    (9, "f06748305bf1b31a0c4b32cc82ce48413a0682b409b00350562fc00240f3f222"),
];
foreach (var (count, expected) in vectors)
{
    tests.Add(("Known answers", $"Independent item vector: {count} leaves", () =>
    {
        string[] values = Enumerable.Range(0, count)
            .Select(i => $"item{i}")
            .ToArray();

        Equal(expected, Convert.ToHexStringLower(MerkleTree.ComputeRoot(values, ExampleTag)));
    }
    ));
}

tests.Add(("Tree structure", "Five-leaf tree assembled explicitly", () =>
{
    byte[] Leaf(string value) => SpecHash(Encoding.UTF8.GetBytes(value));
    byte[] Branch(byte[] left, byte[] right) => SpecHash([.. left, .. right]);

    byte[] ab = Branch(Leaf("aaa"), Leaf("bbb"));
    byte[] cd = Branch(Leaf("ccc"), Leaf("ddd"));
    byte[] ee = Branch(Leaf("eee"), Leaf("eee"));
    string expected = Convert.ToHexStringLower(Branch(Branch(ab, cd), Branch(ee, ee)));

    Equal(ExampleRoot, expected);
    Equal(expected, Convert.ToHexStringLower(MerkleTree.ComputeRoot(example, ExampleTag)));
}
));

tests.Add(("Text encoding", "ASCII and multibyte strings cross chunk boundaries", () =>
{
    foreach (int length in new[] { 4095, 4096, 4097, 8191, 8192, 8193, 100_000 })
    {
        // A following leaf also checks that incremental hash state is reset.
        string[] values = [new string('x', length) + "🙂末尾", "next leaf"];

        Equal(
            Reference(values),
            Convert.ToHexStringLower(MerkleTree.ComputeRoot(values, ExampleTag)),
            $"ASCII prefix length {length}");
    }
}
));

tests.Add(("Text encoding", "Whitespace and embedded nulls are preserved", () =>
{
    foreach (string value in new[] { " a", "a ", "a\n", "a\0" })
    {
        Equal(
            Reference([value]),
            Convert.ToHexStringLower(MerkleTree.ComputeRoot([value], ExampleTag)));

        Different(
            Convert.ToHexStringLower(MerkleTree.ComputeRoot(["a"], ExampleTag)),
            Convert.ToHexStringLower(MerkleTree.ComputeRoot([value], ExampleTag)));
    }
}
));

tests.Add(("Tag selection", "Tag A has an independent known answer", () =>
{
    Equal(
        "e2d7d313d2e64f38e362097ddc2751bd68731aee8a851fa94e7b6a4b327ff5ab",
            Convert.ToHexStringLower(MerkleTree.ComputeRoot([""], "A")));
}
));

tests.Add(("Tag selection", "Custom tags apply to leaves and every branch", () =>
{
    foreach (string tag in new[] { "", "A", "B", "日本語🙂", new string('x', 4097) })
    {
        foreach (int count in new[] { 1, 2, 3, 5, 9, 33 })
        {
            string[] values = Enumerable.Range(0, count)
                .Select(i => $"leaf-{i}")
                .ToArray();

            Equal(
                Reference(values, tag),
                Convert.ToHexStringLower(MerkleTree.ComputeRoot(values, tag)),
                $"Tag length {tag.Length}, {count} leaves");
        }
    }
}
));

tests.Add(("Tag selection", "Tag bytes are preserved and calls do not share tag state", () =>
{
    string[] values = ["aaa", "bbb", "ccc"];
    string[] tags = ["A", "a", " A", "A ", "é", "e\u0301", "A"];
    string[] roots = tags
        .Select(tag => Convert.ToHexStringLower(MerkleTree.ComputeRoot(values, tag)))
        .ToArray();

    for (int i = 0; i < tags.Length; i++)
    {
        Equal(Reference(values, tags[i]), roots[i]);
    }

    if (roots[..^1].Distinct().Count() != tags.Length - 1)
    {
        throw new Exception("Distinct tag encodings should produce distinct roots for this input.");
    }

    Equal(roots[0], roots[^1]);
}
));

tests.Add(("Tag selection", "Null tags are rejected even for an empty tree", () =>
{
    foreach (string[] values in new[] { Array.Empty<string>(), new[] { "aaa" } })
    {
        Equal(
            "tag",
            Throws<ArgumentNullException>(() => MerkleTree.ComputeRoot(values, null!)).ParamName!);
    }
}
));

tests.Add(("Tag selection", "Invalid UTF-16 tags are rejected even for an empty tree", () =>
{
    foreach (string tag in new[] { "\ud800", "\udc00" })
    {
        foreach (string[] values in new[] { Array.Empty<string>(), new[] { "aaa" } })
        {
            Throws<EncoderFallbackException>(() => MerkleTree.ComputeRoot(values, tag));
        }
    }
}
));

tests.Add(("Tag selection", "Empty-tree sentinel is independent of a valid tag", () =>
{
    foreach (string tag in new[] { "", "A", ExampleTag, "日本語🙂" })
    {
        Equal(new string('0', 64), Convert.ToHexStringLower(MerkleTree.ComputeRoot([], tag)));
    }
}
));

tests.AddRange(ProofTests.Cases());

// Run every registered test and report failures without stopping the suite.
int failures = 0;
int number = 0;

Console.WriteLine("Tagged Merkle — test coverage");
Console.WriteLine(new string('=', 60));

foreach (var category in tests.GroupBy(test => test.Category))
{
    Console.WriteLine($"\n{category.Key} ({category.Count()})");

    foreach (var test in category)
    {
        number++;

        try
        {
            test.Run();
            Console.WriteLine($"  {number,2}. PASS  {test.Name}");
        }
        catch (Exception error)
        {
            failures++;
            Console.Error.WriteLine($"  {number,2}. FAIL  {test.Name}\n{error}");
        }
    }
}

Console.WriteLine($"\n{new string('=', 60)}");
Console.WriteLine($"Result: {tests.Count - failures}/{tests.Count} checks passed; {failures} failed.");
Console.WriteLine($"Example root: {Convert.ToHexStringLower(MerkleTree.ComputeRoot(example, ExampleTag))}");
return failures == 0 ? 0 : 1;

// Assertion helpers: returning normally means success; throwing means failure.
static void Equal(string expected, string actual, string context = "")
{
    if (expected != actual)
    {
        throw new Exception($"{context}: expected {expected}, received {actual}.");
    }
}

static void Different(string first, string second)
{
    if (first == second)
    {
        throw new Exception("Expected different roots.");
    }
}

static T Throws<T>(Action action) where T : Exception
{
    try
    {
        action();
    }
    catch (T error)
    {
        return error;
    }

    throw new Exception($"Expected {typeof(T).Name}.");
}

static string RandomText(Random random)
{
    // Select complete text fragments so surrogate pairs are never split.
    string[] alphabet = ["a", "z", "0", " ", "é", "e\u0301", "日", "界", "🙂", "\0"];
    return string.Concat(Enumerable.Range(0, random.Next(0, 64))
        .Select(_ => alphabet[random.Next(alphabet.Length)]));
}

static byte[] SpecHash(byte[] message)
{
    byte[] tag = SHA256.HashData(Encoding.UTF8.GetBytes("Bitcoin_Transaction"));
    return SHA256.HashData([.. tag, .. tag, .. message]);
}

// Deliberately simple functional oracle: immutable level transformations and
// fresh concatenations; shares no production hashing or reduction helpers.
static string Reference(string[] values, string hashTag = "Bitcoin_Transaction")
{
    byte[] tag = SHA256.HashData(new UTF8Encoding(false, true).GetBytes(hashTag));
    byte[] Hash(byte[] message) => SHA256.HashData([.. tag, .. tag, .. message]);

    byte[][] level = values
        .Select(value => Hash(new UTF8Encoding(false, true).GetBytes(value)))
        .ToArray();

    while (level.Length > 1)
    {
        level = level
            .Chunk(2)
            .Select(pair => Hash([.. pair[0], .. pair[^1]]))
            .ToArray();
    }

    return Convert.ToHexStringLower(level.Length == 0 ? new byte[32] : level[0]);
}

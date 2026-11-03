using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using TaggedMerkle;

// A basic local benchmark, not a statistical benchmark framework.
// Run in Release without a debugger; input construction and console output are not timed.
const int warmups = 3;
const int samples = 7;
const string tag = "Bitcoin_Transaction";
(int Leaves, int BytesPerLeaf)[] workloads =
[
    (1_000, 32),
    (100_000, 32),
    (1_000_000, 32),
    (100_000, 20),
    (1_000, 4_096)
];

Console.WriteLine($"Runtime: {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.ProcessArchitecture}");
Console.WriteLine($"UTC: {DateTimeOffset.UtcNow:O}");
Console.WriteLine($"Tag: {tag}; {warmups} warmups; {samples} timed samples per workload.");
Console.WriteLine("Sequential synchronous calls; median time and allocation; range is min–max.");
Console.WriteLine("Managed allocation is cumulative per call, not peak memory or process RSS.");
Console.WriteLine("| Leaves | UTF-8 bytes/leaf | Median ms | Range ms | Payload MiB/s | Managed MiB/call |");
Console.WriteLine("| ---: | ---: | ---: | ---: | ---: | ---: |");

foreach (var (leaves, bytesPerLeaf) in workloads)
{
    // Unique ASCII strings of exact length; each character is one UTF-8 byte.
    string[] values = Enumerable.Range(0, leaves)
        .Select(i => i.ToString("D10", CultureInfo.InvariantCulture).PadRight(bytesPerLeaf, 'x'))
        .ToArray();

    byte[] expected = MerkleTree.ComputeRoot(values, tag);
    for (int i = 1; i < warmups; i++)
        GC.KeepAlive(MerkleTree.ComputeRoot(values, tag));

    double[] elapsedMs = new double[samples];
    long[] allocatedBytes = new long[samples];
    for (int i = 0; i < samples; i++)
    {
        // Begin samples with a consistent collection boundary, outside the timed region.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        byte[] root = MerkleTree.ComputeRoot(values, tag);
        long end = Stopwatch.GetTimestamp();
        allocatedBytes[i] = GC.GetAllocatedBytesForCurrentThread() - before;
        elapsedMs[i] = Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;

        if (!root.AsSpan().SequenceEqual(expected))
            throw new InvalidOperationException("The root changed between benchmark calls.");
    }

    Array.Sort(elapsedMs);
    Array.Sort(allocatedBytes);
    double median = elapsedMs[samples / 2];
    double payloadMiB = (double)leaves * bytesPerLeaf / (1024 * 1024);
    double managedMiB = allocatedBytes[samples / 2] / (1024.0 * 1024);
    Console.WriteLine(FormattableString.Invariant(
        $"| {leaves:N0} | {bytesPerLeaf:N0} | {median:F2} | {elapsedMs[0]:F2}–{elapsedMs[^1]:F2} | {payloadMiB / (median / 1000):F2} | {managedMiB:F2} |"));
}

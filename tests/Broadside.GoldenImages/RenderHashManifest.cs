using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;
using System.Text;
using Broadside.Rendering;

namespace Broadside.GoldenImages;

/// <summary>One page render in a <see cref="RenderHashManifest"/>: its name, its size and the SHA-256 of its pixels.</summary>
/// <param name="Name">The render's name, <see cref="RenderHashManifest.EntryName"/>.</param>
/// <param name="Width">The width in pixels.</param>
/// <param name="Height">The height in pixels.</param>
/// <param name="Hash">The SHA-256 of the pixels in lower-case hexadecimal (<see cref="RenderHashManifest.Hash"/>).</param>
public readonly record struct RenderHashEntry(string Name, int Width, int Height, string Hash);

/// <summary>A render whose hash is not the same in every manifest compared, with each manifest's hash (null where it is missing).</summary>
/// <param name="Name">The render's name.</param>
/// <param name="Hashes">The hash in each manifest, keyed by the manifest's name.</param>
public sealed record RenderHashMismatch(string Name, IReadOnlyDictionary<string, string?> Hashes);

/// <summary>
/// The software rasterizer's cross-platform check (issue #233): the hash of every page of every hand-written corpus file rendered at
/// <see cref="Dpis"/>, written as a manifest on each CI runner and compared across them. Identical package, display list and render
/// options must give identical bytes on every platform, so any difference is a bug in the rasterizer's arithmetic.
/// </summary>
/// <remarks>
/// <para>
/// The manifest is plain text: comment lines starting with <c>#</c> (the platform it was made on), then one line per render, sorted
/// by name: <c>name width×height sha256</c>, separated by tabs. Renders are saved beside it as <c>&lt;name&gt;.png</c> so a comparison
/// can draw diff images of the pages that differ.
/// </para>
/// <para>Spec #33 story 13 (golden images and render determinism).</para>
/// </remarks>
public static class RenderHashManifest
{
    /// <summary>The environment variable naming the directory a run writes its manifest and renders to; unset, nothing is written.</summary>
    public const string OutputVariable = "BROADSIDE_RENDER_HASH_OUTPUT";

    /// <summary>
    /// The environment variable naming a directory with one subdirectory per runner, each holding a manifest and its renders, to compare.
    /// </summary>
    public const string CompareVariable = "BROADSIDE_RENDER_HASH_COMPARE";

    /// <summary>The manifest's file name.</summary>
    public const string FileName = "render-hashes.txt";

    /// <summary>The name of the subdirectory a comparison writes its diff images to, inside the compared directory.</summary>
    public const string DifferencesDirectory = "differences";

    /// <summary>Gets the two resolutions every page is rendered at: 72 DPI (one pixel per point) and 150 DPI (a scale that is not a whole number).</summary>
    public static IReadOnlyList<double> Dpis { get; } = [72, 150];

    /// <summary>Returns the name of one render: the page's snapshot name and the resolution.</summary>
    /// <param name="fileName">The corpus file.</param>
    /// <param name="page">The zero-based page.</param>
    /// <param name="dpi">The resolution.</param>
    /// <returns>The name, for example <c>paths-fill-rules.page0.150dpi</c>.</returns>
    public static string EntryName(string fileName, int page, double dpi) =>
        string.Create(CultureInfo.InvariantCulture, $"{GoldenCorpus.SnapshotName(fileName, page)}.{dpi}dpi");

    /// <summary>Returns the SHA-256 of a bitmap's pixels: the width and height, then each row's <c>width × 4</c> bytes (never the padding).</summary>
    /// <param name="bitmap">The render.</param>
    /// <returns>The hash in lower-case hexadecimal.</returns>
    public static string Hash(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> size = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(size, bitmap.Width);
        BinaryPrimitives.WriteInt32LittleEndian(size[4..], bitmap.Height);
        hash.AppendData(size);
        for (int y = 0; y < bitmap.Height; y++)
        {
            hash.AppendData(bitmap.GetRow(y)[..(bitmap.Width * 4)]);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Describes the platform a manifest is made on: operating system, architecture, runtime and the vector hardware in use.</summary>
    /// <returns>One line.</returns>
    public static string DescribePlatform() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{RuntimeInformation.OSDescription.Trim()}; {RuntimeInformation.ProcessArchitecture}; {RuntimeInformation.FrameworkDescription}; " +
            $"Vector128 accelerated: {Vector128.IsHardwareAccelerated}");

    /// <summary>Writes a manifest, its entries sorted by name.</summary>
    /// <param name="path">The file.</param>
    /// <param name="entries">The renders.</param>
    public static void Write(string path, IEnumerable<RenderHashEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var text = new StringBuilder();
        text.Append("# ").AppendLine(DescribePlatform());
        foreach (RenderHashEntry entry in entries.OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            text.Append(CultureInfo.InvariantCulture, $"{entry.Name}\t{entry.Width}x{entry.Height}\t{entry.Hash}").Append('\n');
        }

        File.WriteAllText(path, text.ToString());
    }

    /// <summary>Reads a manifest.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its entries, keyed by name.</returns>
    /// <exception cref="FormatException">A line is not <c>name width×height hash</c>.</exception>
    public static IReadOnlyDictionary<string, RenderHashEntry> Read(string path)
    {
        var entries = new Dictionary<string, RenderHashEntry>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            string[] fields = line.Split('\t');
            string[] size = fields.Length == 3 ? fields[1].Split('x') : [];
            if (size.Length != 2
                || !int.TryParse(size[0], NumberStyles.None, CultureInfo.InvariantCulture, out int width)
                || !int.TryParse(size[1], NumberStyles.None, CultureInfo.InvariantCulture, out int height))
            {
                throw new FormatException($"'{line}' in {path} is not 'name<TAB>WIDTHxHEIGHT<TAB>sha256'.");
            }

            entries[fields[0]] = new RenderHashEntry(fields[0], width, height, fields[2]);
        }

        return entries;
    }

    /// <summary>Compares manifests: every render whose hash is not the same in all of them, or that some of them lack.</summary>
    /// <param name="manifests">The manifests, keyed by a name for each (the runner).</param>
    /// <returns>The mismatches, sorted by render name; empty when every manifest agrees.</returns>
    public static IReadOnlyList<RenderHashMismatch> Compare(IReadOnlyDictionary<string, IReadOnlyDictionary<string, RenderHashEntry>> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        var mismatches = new List<RenderHashMismatch>();
        IEnumerable<string> names = manifests.Values.SelectMany(manifest => manifest.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        foreach (string name in names)
        {
            var hashes = new SortedDictionary<string, string?>(StringComparer.Ordinal);
            foreach ((string runner, IReadOnlyDictionary<string, RenderHashEntry> manifest) in manifests)
            {
                hashes[runner] = manifest.TryGetValue(name, out RenderHashEntry entry) ? entry.Hash : null;
            }

            if (hashes.Values.Distinct(StringComparer.Ordinal).Skip(1).Any())
            {
                mismatches.Add(new RenderHashMismatch(name, hashes));
            }
        }

        return mismatches;
    }

    /// <summary>
    /// Compares the runs in a directory (one subdirectory per runner with its manifest and renders) and draws a diff image for every
    /// render that differs, between the first runner's render and each other runner's that differs from it, into
    /// <see cref="DifferencesDirectory"/>.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns>The mismatches and a report listing them, one line per render.</returns>
    /// <exception cref="InvalidOperationException">The directory holds fewer than two manifests.</exception>
    public static (IReadOnlyList<RenderHashMismatch> Mismatches, string Report) CompareRuns(string directory)
    {
        var manifests = new SortedDictionary<string, IReadOnlyDictionary<string, RenderHashEntry>>(StringComparer.Ordinal);
        foreach (string run in Directory.GetDirectories(directory))
        {
            string manifest = Path.Combine(run, FileName);
            if (File.Exists(manifest))
            {
                manifests[Path.GetFileName(run)] = Read(manifest);
            }
        }

        if (manifests.Count < 2)
        {
            throw new InvalidOperationException($"{directory} holds {manifests.Count} run(s) with a {FileName}; a comparison needs at least two.");
        }

        IReadOnlyList<RenderHashMismatch> mismatches = Compare(manifests);
        var report = new StringBuilder();
        report.Append(CultureInfo.InvariantCulture, $"Compared {manifests.Count} runs ({string.Join(", ", manifests.Keys)}) ");
        report.Append(CultureInfo.InvariantCulture, $"over {manifests.Values.Max(manifest => manifest.Count)} renders: {mismatches.Count} differ.").Append('\n');
        string differences = Path.Combine(directory, DifferencesDirectory);
        foreach (RenderHashMismatch mismatch in mismatches)
        {
            report.Append(mismatch.Name).Append(':');
            foreach ((string runner, string? hash) in mismatch.Hashes)
            {
                report.Append(CultureInfo.InvariantCulture, $" {runner}={(hash is null ? "missing" : hash[..12])}");
            }

            report.Append('\n');
            DrawDifferences(directory, differences, mismatch, report);
        }

        return (mismatches, report.ToString());
    }

    private static void DrawDifferences(string directory, string differences, RenderHashMismatch mismatch, StringBuilder report)
    {
        (string reference, string? referenceHash) = mismatch.Hashes.First(pair => pair.Value is not null);
        string referencePng = Path.Combine(directory, reference, mismatch.Name + ".png");
        if (!File.Exists(referencePng))
        {
            return;
        }

        GoldenImage expected = GoldenImage.LoadPng(referencePng);
        foreach ((string runner, string? hash) in mismatch.Hashes)
        {
            string png = Path.Combine(directory, runner, mismatch.Name + ".png");
            if (hash is null || hash == referenceHash || !File.Exists(png))
            {
                continue;
            }

            GoldenComparison comparison = GoldenComparison.Compare(expected, GoldenImage.LoadPng(png), GoldenThreshold.Exact);
            Directory.CreateDirectory(differences);
            string stem = Path.Combine(differences, $"{mismatch.Name}.{reference}-vs-{runner}");
            comparison.CreateDiffImage().SavePng(stem + ".diff.png");
            File.Copy(referencePng, stem + "." + reference + ".png", overwrite: true);
            File.Copy(png, stem + "." + runner + ".png", overwrite: true);
            report.Append(CultureInfo.InvariantCulture, $"  {reference} vs {runner}: {comparison.Describe()}").Append('\n');
        }
    }
}

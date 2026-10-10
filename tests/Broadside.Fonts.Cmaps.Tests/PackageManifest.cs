using System.Globalization;

namespace Broadside.Fonts.Cmaps.Tests;

/// <summary>
/// One row of <c>src/Broadside.Fonts.Cmaps/Data/manifest.tsv</c>, written by the generator from the pinned upstream files: the
/// SHA-256 of the upstream bytes, the character collection and writing mode as the file states them, and code to CID samples taken
/// from the collection's <c>cid2code.txt</c> (an independent table).
/// </summary>
internal sealed record PackageManifest(
    FontResourceKind Kind,
    string Name,
    string UpstreamPath,
    int Size,
    string Sha256,
    string Registry,
    string Ordering,
    int Supplement,
    int WritingMode,
    string? UseCMap,
    IReadOnlyList<(string Code, int Cid)> Samples)
{
    private static readonly Lazy<PackageManifest[]> Rows = new(Read);

    /// <summary>Gets every row, CMaps first.</summary>
    public static IReadOnlyList<PackageManifest> All => Rows.Value;

    /// <summary>Gets the row of a CMap.</summary>
    public static PackageManifest CMap(string name) => All.Single(row => row.Kind == FontResourceKind.CMap && row.Name == name);

    private static PackageManifest[] Read()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "manifest.tsv");
        return
        [
            .. File.ReadAllLines(path)
                .Where(line => !line.StartsWith('#') && line.Length > 0)
                .Select(line => line.Split('\t'))
                .Select(cells => new PackageManifest(
                    Enum.Parse<FontResourceKind>(cells[0]),
                    cells[1],
                    cells[2],
                    int.Parse(cells[3], CultureInfo.InvariantCulture),
                    cells[4],
                    cells[5],
                    cells[6],
                    cells[7].Length > 0 ? int.Parse(cells[7], CultureInfo.InvariantCulture) : 0,
                    cells[8].Length > 0 ? int.Parse(cells[8], CultureInfo.InvariantCulture) : 0,
                    cells[9].Length > 0 ? cells[9] : null,
                    [.. cells[11].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(sample => sample.Split('=')).Select(pair => (pair[0], int.Parse(pair[1], CultureInfo.InvariantCulture)))])),
        ];
    }
}

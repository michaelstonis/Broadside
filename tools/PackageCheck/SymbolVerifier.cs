using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

namespace Broadside.Tools.PackageCheck;

/// <summary>
/// Checks a package's symbols: every <c>lib/**.dll</c> has a portable PDB at the same path in the <c>.snupkg</c> beside the package,
/// the PDB's ID matches the assembly's CodeView entry, every source path was mapped to <c>/_/</c> (a CI build), and every source
/// document is either embedded or mapped by SourceLink to a URL at the nuspec's commit.
/// </summary>
public static class SymbolVerifier
{
    /// <summary>The source path a deterministic CI build maps the repository root to.</summary>
    public const string DeterministicRoot = "/_/";

    private const ushort PortableCodeViewMinorVersion = 0x504d;
    private static readonly Guid SourceLinkKind = new("CC110556-A091-4D38-9FEC-25AB9A351A6A");
    private static readonly Guid EmbeddedSourceKind = new("0E8A571B-6926-466E-B4AD-8AB04611F5FE");

    /// <summary>Checks the symbols of <paramref name="package"/>; adds every SourceLink URL to <paramref name="sourceUrls"/> when given.</summary>
    public static IReadOnlyList<Finding> Verify(string packagePath, ZipArchive package, NuspecInfo nuspec, PackagePolicy policy, ICollection<string>? sourceUrls = null)
    {
        string name = Path.GetFileName(packagePath);
        var findings = new List<Finding>();
        void Fail(string message) => findings.Add(new Finding(name, message));

        var assemblies = package.Entries
            .Where(e => e.FullName.StartsWith("lib/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (assemblies.Count == 0)
        {
            return findings;
        }

        string symbolsPath = Path.ChangeExtension(packagePath, ".snupkg");
        if (!File.Exists(symbolsPath))
        {
            Fail($"has no symbol package ({Path.GetFileName(symbolsPath)})");
            return findings;
        }

        using ZipArchive symbols = ZipFile.OpenRead(symbolsPath);
        string urlPrefix = policy.SourceLinkUrlPrefix + nuspec.RepositoryCommit + "/";
        foreach (ZipArchiveEntry assembly in assemblies)
        {
            string pdbPath = Path.ChangeExtension(assembly.FullName, ".pdb");
            ZipArchiveEntry? pdb = symbols.GetEntry(pdbPath);
            if (pdb is null)
            {
                Fail($"symbol package has no {pdbPath}");
                continue;
            }

            foreach (string message in VerifyPair(ReadAll(assembly), ReadAll(pdb), urlPrefix, sourceUrls))
            {
                Fail($"{pdbPath}: {message}");
            }
        }

        return findings;
    }

    /// <summary>Checks one assembly and its PDB; returns the problems.</summary>
    public static IReadOnlyList<string> VerifyPair(byte[] assembly, byte[] pdb, string urlPrefix, ICollection<string>? sourceUrls = null)
    {
        var problems = new List<string>();
        byte[]? expectedId;
        try
        {
            expectedId = ExpectedPdbId(assembly, problems);
        }
        catch (BadImageFormatException ex)
        {
            problems.Add($"the assembly is not a valid PE file ({ex.Message})");
            return problems;
        }

        if (pdb.Length < 4 || pdb[0] != 'B' || pdb[1] != 'S' || pdb[2] != 'J' || pdb[3] != 'B')
        {
            problems.Add("is not a portable PDB");
            return problems;
        }

        using MetadataReaderProvider provider = MetadataReaderProvider.FromPortablePdbImage(ImmutableArray.Create(pdb));
        MetadataReader reader;
        try
        {
            reader = provider.GetMetadataReader();
        }
        catch (BadImageFormatException ex)
        {
            problems.Add($"is not a readable portable PDB ({ex.Message})");
            return problems;
        }

        if (expectedId is not null && !(reader.DebugMetadataHeader?.Id ?? []).AsSpan().SequenceEqual(expectedId))
        {
            problems.Add("does not match its assembly (PDB ID differs from the assembly's CodeView entry)");
        }

        var embedded = new HashSet<DocumentHandle>();
        List<(string Pattern, string Url)>? map = null;
        foreach (CustomDebugInformationHandle handle in reader.CustomDebugInformation)
        {
            CustomDebugInformation information = reader.GetCustomDebugInformation(handle);
            Guid kind = reader.GetGuid(information.Kind);
            if (kind == EmbeddedSourceKind && information.Parent.Kind == HandleKind.Document)
            {
                embedded.Add((DocumentHandle)information.Parent);
            }
            else if (kind == SourceLinkKind && information.Parent.Kind == HandleKind.ModuleDefinition)
            {
                map = ParseSourceLink(reader.GetBlobBytes(information.Value), problems);
            }
        }

        if (map is null)
        {
            problems.Add("has no SourceLink information");
        }

        foreach (DocumentHandle handle in reader.Documents)
        {
            string path = reader.GetString(reader.GetDocument(handle).Name);
            if (!path.StartsWith(DeterministicRoot, StringComparison.Ordinal))
            {
                problems.Add($"source path {path} is not under {DeterministicRoot} (pack with ContinuousIntegrationBuild=true)");
                continue;
            }

            if (embedded.Contains(handle) || map is null)
            {
                continue;
            }

            string? url = Map(map, path);
            if (url is null)
            {
                problems.Add($"source document {path} is neither embedded nor mapped by SourceLink");
            }
            else if (!url.StartsWith(urlPrefix, StringComparison.Ordinal))
            {
                problems.Add($"source document {path} maps to {url}, not under {urlPrefix}");
            }
            else
            {
                sourceUrls?.Add(url);
            }
        }

        return problems;
    }

    private static byte[]? ExpectedPdbId(byte[] assembly, List<string> problems)
    {
        using var pe = new PEReader(ImmutableArray.Create(assembly));
        DebugDirectoryEntry? codeView = null;
        foreach (DebugDirectoryEntry entry in pe.ReadDebugDirectory())
        {
            if (entry.Type == DebugDirectoryEntryType.CodeView)
            {
                codeView = entry;
            }
        }

        if (codeView is not { } cv)
        {
            problems.Add("the assembly has no CodeView debug entry");
            return null;
        }

        if (cv.MinorVersion != PortableCodeViewMinorVersion)
        {
            problems.Add("the assembly was built with a Windows PDB, not a portable one");
            return null;
        }

        CodeViewDebugDirectoryData data = pe.ReadCodeViewDebugDirectoryData(cv);
        byte[] id = new byte[20];
        data.Guid.TryWriteBytes(id);
        BitConverter.TryWriteBytes(id.AsSpan(16), cv.Stamp);
        return id;
    }

    private static List<(string Pattern, string Url)>? ParseSourceLink(byte[] json, List<string> problems)
    {
        try
        {
            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(json));
            var map = new List<(string Pattern, string Url)>();
            foreach (JsonProperty property in document.RootElement.GetProperty("documents").EnumerateObject())
            {
                map.Add((property.Name, property.Value.GetString() ?? string.Empty));
            }

            return map;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            problems.Add($"has unreadable SourceLink JSON ({ex.Message})");
            return null;
        }
    }

    /// <summary>The URL SourceLink gives <paramref name="path"/>: the longest matching pattern wins, a trailing <c>*</c> matches the rest.</summary>
    internal static string? Map(IEnumerable<(string Pattern, string Url)> map, string path)
    {
        string? best = null;
        int bestLength = -1;
        foreach ((string pattern, string url) in map)
        {
            if (pattern.EndsWith('*'))
            {
                string prefix = pattern[..^1];
                if (prefix.Length > bestLength && path.StartsWith(prefix, StringComparison.Ordinal))
                {
                    string rest = path[prefix.Length..].Replace('\\', '/');
                    best = url.EndsWith('*') ? url[..^1] + Uri.EscapeDataString(rest).Replace("%2F", "/", StringComparison.Ordinal) : url;
                    bestLength = prefix.Length;
                }
            }
            else if (string.Equals(pattern, path, StringComparison.Ordinal) && pattern.Length > bestLength)
            {
                best = url;
                bestLength = pattern.Length;
            }
        }

        return best;
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

using System.IO.Compression;
using System.Security;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Broadside.Tools.PackageCheck.Tests;

/// <summary>Builds a synthetic .nupkg (and .snupkg) the way <c>dotnet pack</c> lays them out, with every property a test may break.</summary>
internal sealed class TestPackage
{
    public const string Commit = "0123456789abcdef0123456789abcdef01234567";
    public const string Version = "1.2.3-preview.4";

    public TestPackage(string id) => Id = id;

    public string Id { get; }

    public List<(string Id, string Version)> Dependencies { get; } = [];

    public string LicenseType { get; set; } = "expression";

    public string License { get; set; } = "MIT";

    public string? Readme { get; set; } = "README.md";

    public string? Icon { get; set; } = "icon.png";

    public string RepositoryUrl { get; set; } = "https://github.com/michaelstonis/Broadside";

    public string RepositoryCommit { get; set; } = Commit;

    public List<string> ExtraFiles { get; } = [];

    /// <summary>Files written with the given text.</summary>
    public Dictionary<string, string> Contents { get; } = [];

    /// <summary>The source of the one assembly; its path in the PDB is <see cref="SourcePath"/>.</summary>
    public string Source { get; set; } = "namespace Sample; public sealed class Thing { }";

    public string SourcePath { get; set; } = "/_/src/Sample/Thing.cs";

    /// <summary>The SourceLink JSON, or null for none.</summary>
    public string? SourceLink { get; set; } = SourceLinkFor(Commit);

    public bool EmbedSource { get; set; }

    public bool WriteSymbolPackage { get; set; } = true;

    public bool PdbInSymbolPackage { get; set; } = true;

    /// <summary>Replaces the compiled PDB in the symbol package.</summary>
    public byte[]? PdbOverride { get; set; }

    public static string SourceLinkFor(string commit, string pattern = "/_/*") =>
        $$$"""{"documents":{"{{{pattern}}}":"https://raw.githubusercontent.com/michaelstonis/Broadside/{{{commit}}}/*"}}""";

    public string FileName => $"{Id}.{Version}.nupkg";

    /// <summary>Writes the package (and its symbol package) into <paramref name="directory"/>; returns the .nupkg path.</summary>
    public string WriteTo(string directory)
    {
        (byte[] assembly, byte[] pdb) = Compile(Id, Source, SourcePath, SourceLink, EmbedSource);
        string path = Path.Combine(directory, FileName);
        using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Add(zip, $"{Id}.nuspec", Encoding.UTF8.GetBytes(Nuspec()));
            Add(zip, $"lib/net10.0/{Id}.dll", assembly);
            Add(zip, "_rels/.rels", Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));
            Add(zip, "package/services/metadata/core-properties/x.psmdcp", Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));
            foreach (string file in new[] { Readme, Icon, LicenseType == "file" ? License : null }.OfType<string>().Concat(ExtraFiles).Distinct())
            {
                Add(zip, file, Encoding.UTF8.GetBytes(file));
            }

            foreach ((string file, string text) in Contents)
            {
                Add(zip, file, Encoding.UTF8.GetBytes(text));
            }
        }

        if (WriteSymbolPackage)
        {
            using ZipArchive symbols = ZipFile.Open(Path.ChangeExtension(path, ".snupkg"), ZipArchiveMode.Create);
            Add(symbols, $"{Id}.nuspec", Encoding.UTF8.GetBytes(Nuspec()));
            if (PdbInSymbolPackage)
            {
                Add(symbols, $"lib/net10.0/{Id}.pdb", PdbOverride ?? pdb);
            }
        }

        return path;
    }

    /// <summary>Compiles <paramref name="source"/> deterministically into an assembly and its portable PDB.</summary>
    public static (byte[] Assembly, byte[] Pdb) Compile(string name, string source, string sourcePath, string? sourceLink, bool embedSource)
    {
        SourceText text = SourceText.From(source, Encoding.UTF8);
        CSharpCompilation compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(text, path: sourcePath)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, deterministic: true));
        using var assembly = new MemoryStream();
        using var pdb = new MemoryStream();
        using MemoryStream? link = sourceLink is null ? null : new MemoryStream(Encoding.UTF8.GetBytes(sourceLink));
        EmitResult result = compilation.Emit(
            assembly,
            pdb,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb, pdbFilePath: $"/_/obj/{name}.pdb"),
            embeddedTexts: embedSource ? [EmbeddedText.FromSource(sourcePath, text)] : null,
            sourceLinkStream: link);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return (assembly.ToArray(), pdb.ToArray());
    }

    private string Nuspec()
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?><package xmlns=\"http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd\"><metadata>");
        builder.Append($"<id>{Id}</id><version>{Version}</version>");
        builder.Append($"<license type=\"{LicenseType}\">{SecurityElement.Escape(License)}</license>");
        if (Readme is not null)
        {
            builder.Append($"<readme>{Readme}</readme>");
        }

        if (Icon is not null)
        {
            builder.Append($"<icon>{Icon}</icon>");
        }

        builder.Append($"<repository type=\"git\" url=\"{RepositoryUrl}\" commit=\"{RepositoryCommit}\" />");
        builder.Append("<dependencies><group targetFramework=\"net10.0\">");
        foreach ((string id, string version) in Dependencies)
        {
            builder.Append($"<dependency id=\"{id}\" version=\"{version}\" exclude=\"Build,Analyzers\" />");
        }

        builder.Append("</group></dependencies></metadata></package>");
        return builder.ToString();
    }

    private static void Add(ZipArchive zip, string name, byte[] content)
    {
        using Stream stream = zip.CreateEntry(name).Open();
        stream.Write(content);
    }
}

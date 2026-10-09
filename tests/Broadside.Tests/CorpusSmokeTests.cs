using Broadside.TestSupport;

namespace Broadside.Tests;

/// <summary>Proves the test wiring: the corpus helper finds the files, theories enumerate them, and Verify writes snapshots.</summary>
public class CorpusSmokeTests
{
    [Theory]
    [MemberData(nameof(Corpus.WellFormedFiles), MemberType = typeof(Corpus))]
    public void Well_formed_file_has_pdf_header_and_eof_marker(string fileName)
    {
        ReadOnlySpan<byte> bytes = Corpus.Bytes(fileName);

        Assert.True(bytes.StartsWith("%PDF-"u8), $"{fileName} does not start with %PDF-");
        Assert.True(bytes.TrimEnd("\r\n"u8).EndsWith("%%EOF"u8), $"{fileName} does not end with %%EOF");
    }

    [Theory]
    [MemberData(nameof(Corpus.MalformedFiles), MemberType = typeof(Corpus))]
    public void Malformed_file_exists_and_has_pdf_header(string fileName)
    {
        using FileStream stream = Corpus.Open(fileName);
        Span<byte> header = stackalloc byte[5];

        stream.ReadExactly(header);

        Assert.True(header.SequenceEqual("%PDF-"u8), $"{fileName} does not start with %PDF-");
    }

    [Fact]
    public void File_lists_match_the_corpus_directory()
    {
        string[] onDisk = Directory.GetFiles(Corpus.Directory, "*.pdf").Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;
        string[] listed = Corpus.AllFileNames.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(onDisk, listed);
        Assert.Empty(Corpus.WellFormedFileNames.Intersect(Corpus.MalformedFileNames, StringComparer.Ordinal));
    }

    [Fact]
    public Task Corpus_file_names_match_snapshot()
    {
        IEnumerable<string> fileNames = Directory.GetFiles(Corpus.Directory, "*.pdf").Select(Path.GetFileName).Order(StringComparer.Ordinal)!;

        return Verify(fileNames);
    }

    [Fact]
    public void Package_assembly_loads()
    {
        Assert.Equal("Broadside", typeof(AssemblyMarker).Assembly.GetName().Name);
    }
}

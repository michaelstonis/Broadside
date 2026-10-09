namespace Broadside.TestSupport;

/// <summary>
/// Exposes the hand-written minimal corpus in <c>tests/Corpus/</c> to tests. The file lists mirror the two tables in
/// <c>tests/Corpus/README.md</c>; <c>CorpusSmokeTests</c> in <c>Broadside.Tests</c> checks that they stay in sync with the directory.
/// </summary>
public static class Corpus
{
    private static readonly string[] WellFormed =
    [
        "empty-page.pdf",
        "pdf20-header.pdf",
        "text-standard14.pdf",
        "text-truetype-embedded.pdf",
        "xref-stream.pdf",
        "object-stream.pdf",
        "incremental-update.pdf",
        "hybrid-xref.pdf",
        "linearized.pdf",
        "linearized-xref-stream.pdf",
        "flate-stream.pdf",
        "lzw-stream.pdf",
        "ascii85-stream.pdf",
        "asciihex-stream.pdf",
        "runlength-stream.pdf",
        "filter-chain.pdf",
        "png-predictor.pdf",
        "encrypted-rc4-40.pdf",
        "encrypted-rc4-128.pdf",
        "encrypted-aes-128.pdf",
        "encrypted-aes-256.pdf",
        "encrypted-rc4-40-r3.pdf",
        "encrypted-crypt-filters.pdf",
        "encrypted-aes-gcm.pdf",
        "encrypted-mac.pdf",
        "inline-image.pdf",
        "page-tree-inherited.pdf",
        "annotations-link.pdf",
        "outline.pdf",
        "name-tree-dests.pdf",
        "metadata-xmp.pdf",
    ];

    private static readonly string[] Malformed =
    [
        "broken-xref-offsets.pdf",
        "missing-endobj.pdf",
        "wrong-stream-length.pdf",
        "no-xref.pdf",
        "startxref-wrong.pdf",
        "encrypted-mac-tampered.pdf",
        "encrypted-owner-key-variant.pdf",
    ];

    private static readonly (string FileName, string UserPassword)[] PasswordProtected =
    [
        ("encrypted-user-password.pdf", "p\u00e4sswort"),
        ("encrypted-rc4-user-password.pdf", "caf\u00e9"),
    ];

    /// <summary>The absolute path of <c>tests/Corpus/</c>, found by walking up from the running assembly to the directory that holds <c>Broadside.slnx</c>.</summary>
    public static string Directory => CorpusLocator.CorpusDirectory;

    /// <summary>Names of the files in the "Well-formed files" table of the corpus README.</summary>
    public static IReadOnlyList<string> WellFormedFileNames => WellFormed;

    /// <summary>Names of the files in the "Deliberately broken files" table of the corpus README.</summary>
    public static IReadOnlyList<string> MalformedFileNames => Malformed;

    /// <summary>Names of the files in the "Password-protected files" table: well-formed, but they open only with a password.</summary>
    public static IReadOnlyList<string> PasswordProtectedFileNames { get; } = [.. PasswordProtected.Select(file => file.FileName)];

    /// <summary>Names of every file in the corpus, well-formed first.</summary>
    public static IReadOnlyList<string> AllFileNames { get; } = [.. WellFormed, .. Malformed, .. PasswordProtected.Select(file => file.FileName)];

    /// <summary>Theory data over the password-protected files: the file name and its user password (the owner password is <c>owner</c>).</summary>
    public static TheoryData<string, string> PasswordProtectedFiles => new(PasswordProtected.Select(file => (file.FileName, file.UserPassword)));

    /// <summary>Theory data over <see cref="WellFormedFileNames"/>.</summary>
    public static TheoryData<string> WellFormedFiles => new(WellFormed);

    /// <summary>Theory data over <see cref="MalformedFileNames"/>.</summary>
    public static TheoryData<string> MalformedFiles => new(Malformed);

    /// <summary>Theory data over every file in the corpus.</summary>
    public static TheoryData<string> AllFiles => new(AllFileNames);

    /// <summary>The absolute path of a corpus file.</summary>
    public static string Path(string fileName) => System.IO.Path.Combine(Directory, fileName);

    /// <summary>Reads a corpus file into memory.</summary>
    public static byte[] Bytes(string fileName) => File.ReadAllBytes(Path(fileName));

    /// <summary>Opens a corpus file for reading. The caller disposes the stream.</summary>
    public static FileStream Open(string fileName) => File.OpenRead(Path(fileName));
}

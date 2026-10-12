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
        "text-standard14-differences.pdf",
        "text-standard14-winansi-quirks.pdf",
        "text-standard14-macroman.pdf",
        "text-standard14-symbol.pdf",
        "text-standard14-symbol-differences.pdf",
        "text-standard14-zapfdingbats.pdf",
        "text-standard14-widths.pdf",
        "text-standard14-alias.pdf",
        "text-nonembedded-substitute.pdf",
        "text-type1-symbolic-noencoding.pdf",
        "text-truetype-composite.pdf",
        "text-truetype-symbolic.pdf",
        "text-truetype-macroman.pdf",
        "text-truetype-loca-long.pdf",
        "text-cff-embedded.pdf",
        "text-opentype-cff-embedded.pdf",
        "text-type1-embedded.pdf",
        "text-cid-identity-h.pdf",
        "text-cid-identity-v.pdf",
        "text-cid-embedded-cmap.pdf",
        "text-cidcff-predefined-cmap.pdf",
        "text-tounicode-bf.pdf",
        "text-glyph-names.pdf",
        "xref-stream.pdf",
        "object-stream.pdf",
        "incremental-update.pdf",
        "hybrid-xref.pdf",
        "linearized.pdf",
        "linearized-xref-stream.pdf",
        "linearized-flate-hints.pdf",
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
        "encrypted-empty-owner-password.pdf",
        "encrypted-embedded-file-open.pdf",
        "inline-image.pdf",
        "page-tree-inherited.pdf",
        "annotations-link.pdf",
        "annotations-subtypes.pdf",
        "annotations-appearance.pdf",
        "outline.pdf",
        "name-tree-dests.pdf",
        "metadata-xmp.pdf",
        "metadata-xmp-forms.pdf",
        "info-dictionary.pdf",
        "viewer-preferences.pdf",
        "catalog-version-extensions.pdf",
        "page-labels.pdf",
        "name-tree-deep.pdf",
        "number-tree-deep.pdf",
        "tagged-structure.pdf",
        "destinations-all.pdf",
        "outline-full.pdf",
        "functions.pdf",
        "optional-content.pdf",
        "embedded-files.pdf",
        "collection-portfolio.pdf",
        "associated-files.pdf",
        "object-metadata.pdf",
        "declarations.pdf",
        "actions-all.pdf",
        "actions-preserved.pdf",
        "acroform-fields.pdf",
        "acroform-xfa.pdf",
        "colorspace-families.pdf",
        "color-operators.pdf",
        "default-colorspaces.pdf",
        "separation-special.pdf",
        "form-xobject-nested.pdf",
        "extgstate-params.pdf",
        "marked-content.pdf",
        "image-stencil-mask.pdf",
        "image-explicit-mask.pdf",
        "image-color-key-mask.pdf",
        "image-smask.pdf",
        "image-smask-matte.pdf",
        "image-1bpc.pdf",
        "image-2bpc.pdf",
        "image-4bpc-indexed.pdf",
        "image-16bpc.pdf",
        "image-decode-inverted.pdf",
        "jpx-lossless.pdf",
        "jpx-subsampled.pdf",
        "jpx-smask-in-data.pdf",
        "inline-image-filters.pdf",
        "inline-image-ei-in-data.pdf",
        "dct-baseline.pdf",
        "dct-progressive.pdf",
        "dct-cmyk.pdf",
        "shading-type1-function.pdf",
        "shading-type2-axial.pdf",
        "shading-type3-radial.pdf",
        "shading-type4-freeform.pdf",
        "shading-type5-lattice.pdf",
        "shading-type6-coons.pdf",
        "shading-type7-tensor.pdf",
        "pattern-tiling-colored.pdf",
        "pattern-tiling-uncolored.pdf",
        "pattern-shading-axial.pdf",
        "pattern-in-form.pdf",
        "text-type3.pdf",
        "ccitt-g3-1d.pdf",
        "ccitt-g3-1d-eol-align.pdf",
        "ccitt-g3-2d.pdf",
        "ccitt-g4.pdf",
        "ccitt-g4-no-eob.pdf",
        "ccitt-g4-align.pdf",
        "ccitt-blackis1-mask.pdf",
        "ccitt-inline.pdf",
        "jbig2-generic.pdf",
        "jbig2-generic-mmr.pdf",
        "jbig2-annex-h.pdf",
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
        "encrypted-rc4-length-missing.pdf",
        "name-tree-broken.pdf",
        "outline-broken.pdf",
        "annotations-malformed.pdf",
        "text-type1-pfb.pdf",
        "text-type1-hex-eexec.pdf",
        "text-type1-bad-lengths.pdf",
        "pattern-recursive.pdf",
        "shading-mesh-truncated.pdf",
        "text-type3-recursive.pdf",
        "ccitt-g3-damaged.pdf",
        "ccitt-g4-truncated.pdf",
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

using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Tests.Document;

namespace Broadside.Tests.Files;

/// <summary>File specifications in string and dictionary form, embedded file streams and related files. ISO 32000-2 §7.11.</summary>
public class FileSpecificationTests
{
    private static PdfDocument OpenWith(params string[] extraObjects) => OpenWith(new PdfOptions(), extraObjects);

    private static PdfDocument OpenWith(PdfOptions options, params string[] extraObjects) => PdfDocument.Open(
        new TestPdf().Build(
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /Resources << >> /MediaBox [0 0 612 792] >>",
            .. extraObjects,
        ]),
        options);

    [Fact]
    public void A_string_file_specification_splits_into_components_with_escaped_solidus_kept()
    {
        using PdfDocument document = OpenWith();

        PdfFileSpecification spec = document.GetFileSpecification(new CosString("/HardDisk/in\\/out/Summary.pdf"u8))!;

        Assert.Null(spec.Dictionary);
        Assert.Equal("/HardDisk/in\\/out/Summary.pdf", spec.FileName);
        Assert.True(spec.IsAbsolute);
        Assert.Equal(["HardDisk", "in/out", "Summary.pdf"], spec.PathComponents);
        Assert.Equal("Summary.pdf", spec.SafeFileName);
        Assert.Equal(PdfFileRelationship.Unspecified, spec.Relationship);
        Assert.Null(spec.EmbeddedFile);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_relative_file_specification_keeps_its_parent_components_raw()
    {
        using PdfDocument document = OpenWith();

        PdfFileSpecification spec = document.GetFileSpecification(new CosString("../../ArtFiles/Figure1.pdf"u8))!;

        Assert.False(spec.IsAbsolute);
        Assert.Equal(["..", "..", "ArtFiles", "Figure1.pdf"], spec.PathComponents);
        Assert.Equal("Figure1.pdf", spec.SafeFileName);
    }

    [Theory]
    [MemberData(nameof(UnsafeNames))]
    public void An_unsafe_file_name_is_sanitised(string raw, string? expected)
    {
        using PdfDocument document = OpenWith();

        PdfFileSpecification spec = document.GetFileSpecification(new CosString(System.Text.Encoding.Latin1.GetBytes(raw)))!;

        Assert.Equal(expected, spec.SafeFileName);
    }

    public static TheoryData<string, string?> UnsafeNames => new()
    {
        { "C:\\Windows\\evil.exe", "evil.exe" },
        { "..", null },
        { "dir/", null },
        { "CON", "_CON" },
        { "lpt1.txt", "_lpt1.txt" },
        { "a*b?c<d>e|f\"g:h.txt...", "a_b_c_d_e_f_g_h.txt" },
        { "nul\0byte.txt", "nul_byte.txt" },
    };

    [Fact]
    public void A_dictionary_prefers_UF_over_F_and_names_its_embedded_stream_by_the_same_key()
    {
        using PdfDocument document = OpenWith(
            "<< /Type /Filespec /F (plain.txt) /UF <FEFF00E9002E007400780074> /Desc (A note) /AFRelationship /Source /V true "
            + "/ID [<01> <02>] /EF << /F 6 0 R /UF 5 0 R >> >>",
            "<< /Type /EmbeddedFile /Subtype /text#2Fplain /Params << /Size 5 /CheckSum <5d41402abc4b2a76b9719d911017c592> >> /Length 5 >>\nstream\nhello\nendstream",
            "<< /Length 3 >>\nstream\nold\nendstream");

        PdfFileSpecification spec = document.GetFileSpecification(new CosReference(4, 0))!;

        Assert.Equal(new CosReference(4, 0), spec.Reference);
        Assert.Equal("\u00e9.txt", spec.FileName);
        Assert.Equal("\u00e9.txt", spec.SafeFileName);
        Assert.Equal("A note", spec.Description);
        Assert.True(spec.IsVolatile);
        Assert.Equal(PdfFileRelationship.Source, spec.Relationship);
        Assert.Equal(2, spec.FileIdentifier!.Count);
        PdfEmbeddedFile file = spec.EmbeddedFile!;
        Assert.Equal(new CosReference(5, 0), file.Reference);
        Assert.Equal("text/plain", file.Subtype);
        Assert.Equal("text/plain", file.MediaType);
        Assert.Equal("hello"u8.ToArray(), file.Decode().ToArray());
        Assert.Equal(5, file.Parameters!.Size);
        Assert.Equal(PdfCheckSumStatus.Matches, file.VerifyCheckSum());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_wrong_checksum_is_a_result_not_a_diagnostic()
    {
        using PdfDocument document = OpenWith(
            "<< /Type /Filespec /F (a.txt) /EF << /F 5 0 R >> >>",
            "<< /Params << /CheckSum <00000000000000000000000000000000> >> /Length 5 >>\nstream\nhello\nendstream");

        PdfEmbeddedFile file = document.GetFileSpecification(new CosReference(4, 0))!.EmbeddedFile!;

        Assert.Equal(PdfCheckSumStatus.DoesNotMatch, file.VerifyCheckSum());
        Assert.Null(file.Subtype);
        Assert.Equal("application/octet-stream", file.MediaType);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Name_preference_falls_back_through_F_Unix_Mac_and_DOS()
    {
        using PdfDocument document = OpenWith(
            "<< /DOS (DOS.TXT) /Mac (mac.txt) /Unix (unix.txt) >>",
            "<< /DOS (DOS.TXT) /Mac (mac.txt) >>",
            "<< /DOS (DOS.TXT) >>");

        Assert.Equal("unix.txt", document.GetFileSpecification(new CosReference(4, 0))!.FileName);
        Assert.Equal("mac.txt", document.GetFileSpecification(new CosReference(5, 0))!.FileName);
        Assert.Equal("DOS.TXT", document.GetFileSpecification(new CosReference(6, 0))!.FileName);
    }

    [Fact]
    public void A_URL_file_specification_is_recognised_by_its_file_system()
    {
        using PdfDocument document = OpenWith("<< /FS /URL /F (https://example.com/a%20b.pdf) >>");

        PdfFileSpecification spec = document.GetFileSpecification(new CosReference(4, 0))!;

        Assert.True(spec.IsUrl);
        Assert.Equal("URL", spec.FileSystem!.Value);
        Assert.Equal("https://example.com/a%20b.pdf", spec.FileName);
    }

    [Fact]
    public void A_second_class_relationship_reads_as_other_with_its_name()
    {
        using PdfDocument document = OpenWith("<< /F (a.txt) /AFRelationship /ABCD_Witness >>");

        PdfFileSpecification spec = document.GetFileSpecification(new CosReference(4, 0))!;

        Assert.Equal(PdfFileRelationship.Other, spec.Relationship);
        Assert.Equal("ABCD_Witness", spec.RelationshipName.Value);
    }

    [Fact]
    public void Related_files_pair_names_with_embedded_file_streams()
    {
        using PdfDocument document = OpenWith(
            "<< /Type /Filespec /F (main.c) /EF << /F 5 0 R >> /RF << /F 6 0 R >> >>",
            "<< /Length 4 >>\nstream\nmain\nendstream",
            "[(util.h) 7 0 R]",
            "<< /Length 4 >>\nstream\nutil\nendstream");

        PdfRelatedFile related = Assert.Single(document.GetFileSpecification(new CosReference(4, 0))!.RelatedFiles);

        Assert.Equal("util.h", related.Name);
        Assert.Equal("util"u8.ToArray(), related.File.Decode().ToArray());
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void A_malformed_related_files_array_keeps_its_well_formed_pairs_with_a_diagnostic()
    {
        using PdfDocument document = OpenWith(
            "<< /Type /Filespec /F (main.c) /EF << /F 5 0 R >> /RF << /F [(util.h) 5 0 R (bad) 42 (odd)] >> >>",
            "<< /Length 4 >>\nstream\nmain\nendstream");

        PdfRelatedFile related = Assert.Single(document.GetFileSpecification(new CosReference(4, 0))!.RelatedFiles);

        Assert.Equal("util.h", related.Name);
        Assert.Equal(["RelatedFilesInvalid"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
        Assert.Equal(new CosReference(4, 0), document.Diagnostics[0].ObjectReference);
    }

    [Fact]
    public void An_EF_without_a_stream_is_reported_and_the_specification_is_still_readable()
    {
        using PdfDocument document = OpenWith("<< /Type /Filespec /F (gone.txt) /EF << /F 99 0 R >> >>");

        PdfFileSpecification spec = document.GetFileSpecification(new CosReference(4, 0))!;

        Assert.Equal("gone.txt", spec.FileName);
        Assert.Null(spec.EmbeddedFile);
        Assert.Equal(["EmbeddedFileMissing"], document.Diagnostics.Select(diagnostic => diagnostic.Code));
    }

    [Fact]
    public void Strict_mode_throws_for_an_EF_without_a_stream()
    {
        using PdfDocument document = OpenWith(new PdfOptions().UseStrict(), "<< /Type /Filespec /F (gone.txt) /EF << /F 99 0 R >> >>");

        PdfFileSpecification spec = document.GetFileSpecification(new CosReference(4, 0))!;

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => spec.EmbeddedFile);
        Assert.Equal("EmbeddedFileMissing", exception.Diagnostic.Code);
    }

    [Fact]
    public void A_value_that_is_neither_a_string_nor_a_dictionary_is_not_a_file_specification()
    {
        using PdfDocument document = OpenWith();

        Assert.Null(document.GetFileSpecification(new CosInteger(3)));
        Assert.Null(document.GetFileSpecification(new CosReference(99, 0)));
    }

    [Fact]
    public void Reading_a_file_specification_leaves_every_object_clean()
    {
        using PdfDocument document = OpenWith(
            "<< /Type /Filespec /F (main.c) /UF (main.c) /EF << /F 5 0 R >> /RF << /F 6 0 R >> /CI 8 0 R >>",
            "<< /Params << /Size 4 /ModDate (D:20240101) >> /Length 4 >>\nstream\nmain\nendstream",
            "[(util.h) 7 0 R]",
            "<< /Length 4 >>\nstream\nutil\nendstream",
            "<< /Type /CollectionItem /name (Main) >>");

        PdfFileSpecification spec = document.GetFileSpecification(new CosReference(4, 0))!;
        _ = (spec.FileName, spec.SafeFileName, spec.PathComponents, spec.Relationship, spec.Description, spec.IsVolatile,
            spec.EmbeddedFile!.Parameters!.Size, spec.EmbeddedFile.MediaType, spec.EmbeddedFile.VerifyCheckSum(), spec.RelatedFiles,
            spec.CollectionItem, spec.Thumbnail, spec.EncryptedPayload);

        for (int number = 1; number <= 8; number++)
        {
            Assert.False(document.Resolve(new CosReference(number, 0)).IsDirty);
        }
    }
}

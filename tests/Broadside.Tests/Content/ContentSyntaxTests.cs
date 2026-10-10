using Broadside.Content;
using Broadside.Diagnostics;

namespace Broadside.Tests.Content;

/// <summary>
/// Content stream syntax as the interpreter reads it, and the repairs it makes for malformed content: operands, glued tokens,
/// compatibility sections, unknown operators and operand-count recovery. ISO 32000-2 §7.8.2, Table 33.
/// </summary>
public class ContentSyntaxTests
{
    [Fact]
    public void Operators_are_reported_with_their_operands_of_every_type()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run(
            "/Span << /MCID 3 /Alt (a\\)b) /On true /Off false /N null >> BDC [(A) -250 <4142> 1.5] TJ EMC",
            ContentEvents.Operators);

        Assert.Equal(["Op /Span <</MCID 3 /Alt (a)b) /On true /Off false /N null>> BDC", "Op [(A) -250 (AB) 1.5] TJ", "Op EMC"], events.Body);
        Assert.Equal(["ContentOperatorOutOfContext", "ContentFontMissing"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void Operator_events_give_the_source_range_in_the_decoded_part()
    {
        var probe = new OperatorProbe();
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("q", "  1 0 0 1 5 5 cm Q"));

        document.Pages[0].ProcessContent(probe);

        Assert.Equal([(0, 0, 1, 0), (1, 2, 14, 14), (1, 17, 1, 17)], probe.Ranges);
        Assert.Equal([4, 5, 5], probe.Streams);
    }

    public static TheoryData<string, string[]> GluedTokens => new()
    {
        { "q1 0 0 1 0 0cm Q", ["Op q", "Op 1 0 0 1 0 0 cm", "Op Q"] },
        { "Qq", ["Op Q", "Op q"] },
        { "BTq ETQ", ["Op BT", "Op q", "Op ET", "Op Q"] },
        { "0 0 1 rg1 0 0 RG", ["Op 0 0 1 rg", "Op 1 0 0 RG"] },
    };

    [Theory]
    [MemberData(nameof(GluedTokens))]
    public void Glued_operators_and_numbers_are_split_into_their_tokens(string content, string[] expected)
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run(content, ContentEvents.Operators);

        Assert.Equal(expected, events.Body);
        Assert.Contains("ContentGluedTokens", ContentPdf.Codes(diagnostics));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Code == "ContentGluedTokens" && diagnostic.Severity != DiagnosticSeverity.Information);
    }

    [Fact]
    public void A_glued_save_keeps_its_state_change()
    {
        (RecordingProcessor events, _) = ContentPdf.Run("q2 0 0 2 0 0 cm 0 0 m 1 1 l S Q", ContentEvents.Paths);

        Assert.Equal(["Paint Stroke NonZero M 0,0 L 1,1 ctm=[2 0 0 2 0 0]"], events.Body);
    }

    [Fact]
    public void A_keyword_that_does_not_split_into_operators_is_one_unknown_operator()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("1 2 fxyz 0 0 m 1 1 l S", ContentEvents.Operators | ContentEvents.Paths);

        Assert.Equal(["Op 1 2 fxyz", "Op 0 0 m", "Op 1 1 l", "Op S", "Paint Stroke NonZero M 0,0 L 1,1 ctm=[1 0 0 1 0 0]"], events.Body);
        Assert.Equal(["ContentUnknownOperator"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void Unknown_operators_and_their_operands_are_ignored_silently_inside_a_compatibility_section()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("BX 1 2 newop /X sparkle EX 0 0 m 1 1 l S", ContentEvents.Paths);

        Assert.Equal(["Paint Stroke NonZero M 0,0 L 1,1 ctm=[1 0 0 1 0 0]"], events.Body);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Compatibility_sections_nest()
    {
        (_, var diagnostics) = ContentPdf.Run("BX BX newop EX newop EX");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void An_unknown_operator_outside_a_compatibility_section_is_recorded_and_its_operands_do_not_leak()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("BX EX 7 newop 0 0 m 1 1 l S", ContentEvents.Paths);

        Assert.Equal(["Paint Stroke NonZero M 0,0 L 1,1 ctm=[1 0 0 1 0 0]"], events.Body);
        Assert.Equal(["ContentUnknownOperator"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void Ex_without_bx_and_bx_without_ex_are_recorded()
    {
        Assert.Equal(["ContentCompatibilityUnbalanced"], ContentPdf.Codes(ContentPdf.Run("EX").Diagnostics));
        Assert.Equal(["ContentCompatibilityUnbalanced"], ContentPdf.Codes(ContentPdf.Run("BX").Diagnostics));
    }

    [Fact]
    public void An_operator_with_too_few_operands_is_skipped()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("2 0 0 2 cm 0 0 m 1 1 l S", ContentEvents.Paths);

        Assert.Equal(["Paint Stroke NonZero M 0,0 L 1,1 ctm=[1 0 0 1 0 0]"], events.Body);
        Assert.Equal(["ContentOperandCount"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void An_operator_with_too_many_operands_uses_the_last_ones()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("9 9 0 0 m 1 1 l S", ContentEvents.Paths);

        Assert.Equal(["Paint Stroke NonZero M 0,0 L 1,1 ctm=[1 0 0 1 0 0]"], events.Body);
        Assert.Equal(["ContentOperandCount"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void An_operand_of_the_wrong_type_skips_the_operator()
    {
        (StateRecorder recorder, var diagnostics) = StateRecorder.Run("/Thick w (3) 2 d 0 0 m 1 1 l S");

        Assert.Equal(1, Assert.Single(recorder.States).LineWidth);
        Assert.Empty(recorder.DashArrays[0]);
        Assert.Equal(["ContentOperandType"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void Operands_left_at_the_end_of_the_stream_are_recorded()
    {
        Assert.Equal(["ContentOperandCount"], ContentPdf.Codes(ContentPdf.Run("q Q 1 2").Diagnostics));
    }

    [Fact]
    public void Stray_closing_delimiters_are_skipped()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("] >> ) 0 0 m 1 1 l S", ContentEvents.Paths);

        Assert.Single(events.Body);
        Assert.Equal(["ContentSyntaxInvalid"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void An_array_left_open_is_closed_at_its_operator()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("[3 1 2 d 0 0 m 1 1 l S", ContentEvents.Operators);

        Assert.Equal("Op [3 1 2] d", events.Body[0]);
        Assert.Equal(["ContentSyntaxInvalid", "ContentOperandCount"], ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void Lenient_numbers_are_read_as_the_object_parser_reads_them()
    {
        (RecordingProcessor events, var diagnostics) = ContentPdf.Run("--5 5. .5 cm", ContentEvents.Operators);

        Assert.Equal(["Op -5 5 0.5 cm"], events.Body);
        Assert.Contains("ContentSyntaxInvalid", ContentPdf.Codes(diagnostics));
    }

    [Fact]
    public void Graphics_state_operators_inside_a_path_object_apply_with_an_information_diagnostic()
    {
        (StateRecorder recorder, var diagnostics) = StateRecorder.Run("0 0 m 1 1 l 2 w S");

        Assert.Equal(2, Assert.Single(recorder.States).LineWidth);
        Diagnostic diagnostic = Assert.Single(diagnostics);
        Assert.Equal("ContentOperatorOutOfContext", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Information, diagnostic.Severity);
    }

    [Fact]
    public void An_inline_image_is_one_operator_and_its_data_is_never_read_as_operators()
    {
        var probe = new OperatorProbe();
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("q BI /W 2 /H 1 /BPC 8 /CS /G ID \u0001Q EI\u0002 EI Q"));

        document.Pages[0].ProcessContent(probe);

        Assert.Equal(["q", "BI", "Q"], probe.Keywords);
        Assert.Equal("\u0001Q EI\u0002", probe.InlineData);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void An_inline_image_length_entry_finds_the_end_of_its_data()
    {
        var probe = new OperatorProbe();
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("BI /W 4 /H 1 /BPC 8 /CS /G /L 4 ID EI x EI Q"));

        document.Pages[0].ProcessContent(probe);

        Assert.Equal(["BI", "Q"], probe.Keywords);
        Assert.Equal("EI x", probe.InlineData);
    }

    [Fact]
    public void Strict_mode_throws_the_first_content_deviation()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("0 0 m 1 1 l S Q"), new PdfOptions().UseStrict());

        DiagnosticException exception = Assert.Throws<DiagnosticException>(() => document.Pages[0].ProcessContent(new RecordingProcessor()));

        Assert.Equal("ContentStackUnderflow", exception.Diagnostic.Code);
        Assert.Equal(new Broadside.Objects.CosReference(4, 0), exception.Diagnostic.ObjectReference);
    }

    [Fact]
    public void Each_kind_of_deviation_is_recorded_once_per_stream()
    {
        (_, var diagnostics) = ContentPdf.Run("Q Q Q newop newop");

        Assert.Equal(["ContentStackUnderflow", "ContentUnknownOperator"], ContentPdf.Codes(diagnostics));
        Assert.Contains("decoded offset 0", diagnostics[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_canceled_token_stops_the_run()
    {
        using PdfDocument document = PdfDocument.Open(ContentPdf.Build("q Q"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => document.Pages[0].ProcessContent(new RecordingProcessor(), new ContentOptions().WithCancellation(cancellation.Token)));
    }
}

/// <summary>Records the keyword, source range and stream of every operator.</summary>
internal sealed class OperatorProbe : ContentProcessor
{
    public List<string> Keywords { get; } = [];

    public List<(int Part, int Offset, int Length, int KeywordOffset)> Ranges { get; } = [];

    public List<int> Streams { get; } = [];

    public string InlineData { get; private set; } = string.Empty;

    public override ContentEvents Events => ContentEvents.Operators;

    public override void VisitOperator(in ContentOperator op, ContentContext context)
    {
        Keywords.Add(System.Text.Encoding.Latin1.GetString(op.Keyword));
        Ranges.Add((op.PartIndex, op.Offset, op.Length, op.KeywordOffset));
        Streams.Add(op.Stream?.ObjectNumber ?? 0);
        if (op.Code == ContentOperatorCode.BeginInlineImage)
        {
            InlineData = System.Text.Encoding.Latin1.GetString(op.Data);
        }
    }
}

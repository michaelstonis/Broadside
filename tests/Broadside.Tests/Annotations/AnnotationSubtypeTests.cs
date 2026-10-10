using Broadside.Annotations;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.Tests.Document;
using Broadside.TestSupport;

namespace Broadside.Tests.Annotations;

/// <summary>Every annotation type of ISO 32000-2 Table 171 and its entries (§12.5.6, Tables 172 to 195, 309, 333, 398, 403).</summary>
public class AnnotationSubtypeTests
{
    [Fact]
    public void Annotations_subtypes_pdf_maps_each_standard_subtype_to_its_class_and_an_unknown_one_to_the_generic_class()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-subtypes.pdf"));

        IReadOnlyList<PdfAnnotation> annotations = document.Pages[0].Annotations;

        Assert.Equal(
            [
                "Text PdfTextAnnotation", "Popup PdfPopupAnnotation", "Text PdfTextAnnotation", "Link PdfLinkAnnotation",
                "FreeText PdfFreeTextAnnotation", "Line PdfLineAnnotation", "Square PdfSquareAnnotation", "Circle PdfCircleAnnotation",
                "Polygon PdfPolygonAnnotation", "PolyLine PdfPolyLineAnnotation", "Highlight PdfHighlightAnnotation",
                "Underline PdfUnderlineAnnotation", "Squiggly PdfSquigglyAnnotation", "StrikeOut PdfStrikeOutAnnotation",
                "Caret PdfCaretAnnotation", "Stamp PdfStampAnnotation", "Ink PdfInkAnnotation",
                "FileAttachment PdfFileAttachmentAnnotation", "Sound PdfSoundAnnotation", "Movie PdfMovieAnnotation",
                "Screen PdfScreenAnnotation", "Widget PdfWidgetAnnotation", "PrinterMark PdfPrinterMarkAnnotation",
                "Watermark PdfWatermarkAnnotation", "ThreeD Pdf3DAnnotation", "Redact PdfRedactAnnotation",
                "Projection PdfProjectionAnnotation", "RichMedia PdfRichMediaAnnotation", "Unknown PdfUnknownAnnotation",
                "TrapNet PdfTrapNetAnnotation",
            ],
            annotations.Select(annotation => $"{annotation.Kind} {annotation.GetType().Name}"));
        Assert.Equal(28, annotations.Select(annotation => annotation.Kind).Where(kind => kind != PdfAnnotationKind.Unknown).Distinct().Count());
        Assert.Equal(new CosName("XBroadsideTest"), annotations[28].Subtype);
        Assert.Equal(new CosName("3D"), annotations[24].Subtype);
        Assert.All(annotations, annotation => Assert.Same(document.Pages[0], annotation.Page));
        Assert.Same(annotations[0], document.Pages[0].Annotations[0]);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Reading_every_entry_of_every_annotation_records_nothing_and_changes_nothing()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-subtypes.pdf"));

        int read = AnnotationWalker.Walk(document);

        Assert.Equal(30, read);
        Assert.Empty(document.Diagnostics);
        Assert.False(document.Pages[0].Dictionary.IsDirty);
        Assert.All(document.Pages[0].Annotations, annotation => Assert.False(annotation.Dictionary.IsDirty));
    }

    [Fact]
    public void Concurrent_readers_get_the_same_view_for_each_annotation()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-subtypes.pdf"));
        var results = new PdfAnnotation?[64][];

        Parallel.For(0, results.Length, index =>
        {
            IReadOnlyList<PdfAnnotation> annotations = document.Pages[0].Annotations;
            results[index] = [.. annotations, ((PdfTextAnnotation)annotations[2]).InReplyTo, ((PdfPopupAnnotation)annotations[1]).Parent];
        });

        Assert.All(results, result => Assert.Equal(results[0], result, ReferenceEqualityComparer.Instance));
        Assert.Same(results[0][0], results[0][30]);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Screen_and_widget_annotations_expose_their_trigger_event_actions()
    {
        byte[] file = new TestPdf().Build(
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << >> /Annots [4 0 R 5 0 R] >>",
            "<< /Type /Annot /Subtype /Screen /Rect [0 0 10 10] /P 3 0 R /AA << /PO << /S /URI /URI (https://example.org/open) >> >> >>",
            "<< /Type /Annot /Subtype /Widget /Rect [0 0 10 10] /P 3 0 R /AA << /Fo << /S /URI /URI (https://example.org/focus) >> >> >>");
        using PdfDocument document = PdfDocument.Open(file);
        IReadOnlyList<PdfAnnotation> annotations = document.Pages[0].Annotations;

        PdfAnnotationAdditionalActions screen = Assert.IsType<PdfScreenAnnotation>(annotations[0]).AdditionalActions!;
        PdfAnnotationAdditionalActions widget = Assert.IsType<PdfWidgetAnnotation>(annotations[1]).AdditionalActions!;

        Assert.Equal("https://example.org/open", Assert.IsType<PdfUriAction>(screen.PageOpen).Uri);
        Assert.Equal("https://example.org/focus", Assert.IsType<PdfUriAction>(widget.Focus).Uri);
        Assert.Null(widget.CursorEnter);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Flags_colours_borders_and_markup_entries_are_exposed()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-subtypes.pdf"));
        IReadOnlyList<PdfAnnotation> annotations = document.Pages[0].Annotations;

        PdfTextAnnotation note = Assert.IsType<PdfTextAnnotation>(annotations[0]);
        Assert.Equal(PdfAnnotationFlags.Print | PdfAnnotationFlags.NoZoom | PdfAnnotationFlags.NoRotate, note.Flags);
        Assert.Equal(new PdfDeviceColor(1, 1, 0), note.Color);
        Assert.Equal(PdfDeviceColorSpace.Rgb, note.Color!.ColorSpace);
        Assert.Equal(("Note text", "note-1", "Alice", "Review"), (note.Contents, note.Name, note.Title, note.Subject));
        Assert.Equal("D:20240102030405Z", note.ModifiedText);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero), note.Modified?.Value);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero), note.CreationDate?.Value);
        Assert.Equal(("Comment", true), (note.IconName, note.IsOpen));
        Assert.Null(note.State);
        Assert.Equal(PdfBorderSource.Default, note.Border.Source);
        Assert.Equal((1.0, PdfBorderStyle.Solid), (note.Border.Width, note.Border.Style));

        PdfAnnotation unknown = annotations[28];
        Assert.Equal(
            PdfAnnotationFlags.Invisible | PdfAnnotationFlags.Hidden | PdfAnnotationFlags.Print | PdfAnnotationFlags.NoZoom
            | PdfAnnotationFlags.NoRotate | PdfAnnotationFlags.NoView | PdfAnnotationFlags.ReadOnly | PdfAnnotationFlags.Locked
            | PdfAnnotationFlags.ToggleNoView | PdfAnnotationFlags.LockedContents,
            unknown.Flags);

        PdfSquareAnnotation square = Assert.IsType<PdfSquareAnnotation>(annotations[6]);
        Assert.Equal((2.0, PdfBorderStyle.Dashed, PdfBorderSource.BorderStyle), (square.Border.Width, square.Border.Style, square.Border.Source));
        Assert.Equal([3.0, 2.0], square.Border.DashPattern);
        Assert.Equal(new PdfDeviceColor(0, 0, 1), square.InteriorColor);
        Assert.Equal((PdfBorderEffectStyle.Cloudy, 1.0), (square.BorderEffect!.Style, square.BorderEffect.Intensity));
        Assert.Equal(new PdfRectangleDifferences(1, 2, 3, 4), square.RectangleDifferences);

        PdfCircleAnnotation circle = Assert.IsType<PdfCircleAnnotation>(annotations[7]);
        Assert.Equal((2.0, PdfBorderStyle.Dashed, PdfBorderSource.BorderArray), (circle.Border.Width, circle.Border.Style, circle.Border.Source));
        Assert.Equal([4.0, 1.0], circle.Border.DashPattern);
        Assert.Equal(PdfDeviceColorSpace.Gray, circle.InteriorColor!.ColorSpace);
        Assert.Equal(PdfDeviceColorSpace.Cmyk, circle.Color!.ColorSpace);
        Assert.Null(circle.BorderEffect);

        PdfLinkAnnotation link = Assert.IsType<PdfLinkAnnotation>(annotations[3]);
        Assert.Equal((2.0, PdfBorderStyle.Underline), (link.Border.Width, link.Border.Style));
        Assert.Equal(PdfHighlightMode.Outline, link.HighlightMode);
        Assert.Equal(new PdfQuadrilateral(new PathPoint(222, 702), new PathPoint(278, 702), new PathPoint(278, 738), new PathPoint(222, 738)), Assert.Single(link.QuadPoints));
        Assert.Equal(link.QuadPoints, link.ActivationRegion);
        PdfExplicitDestination destination = Assert.IsType<PdfExplicitDestination>(link.Destination);
        Assert.Equal((PdfDestinationView.Fit, (int?)0), (destination.View, destination.PageIndex));
        Assert.Null(link.Action);

        PdfStrikeOutAnnotation strikeOut = Assert.IsType<PdfStrikeOutAnnotation>(annotations[13]);
        Assert.Equal(2, strikeOut.QuadPoints.Count);
        Assert.Equal(new PdfRectangle(60, 70, 90, 80), strikeOut.QuadPoints[1].Bounds);
        Assert.Equal(new PathPoint(10, 10), Assert.IsType<PdfHighlightAnnotation>(annotations[10]).QuadPoints[0].Point1);

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Popup_and_parent_refer_to_each_other_and_replies_carry_their_state()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-subtypes.pdf"));
        IReadOnlyList<PdfAnnotation> annotations = document.Pages[0].Annotations;

        PdfTextAnnotation note = (PdfTextAnnotation)annotations[0];
        PdfPopupAnnotation popup = Assert.IsType<PdfPopupAnnotation>(note.Popup);
        Assert.Same(annotations[1], popup);
        Assert.Same(note, popup.Parent);
        Assert.True(popup.IsOpen);
        Assert.Null(popup.AppearanceDictionary);

        PdfTextAnnotation reply = Assert.IsType<PdfTextAnnotation>(annotations[2]);
        Assert.Same(note, reply.InReplyTo);
        Assert.Equal(PdfReplyType.Reply, reply.ReplyType);
        Assert.Equal(("Accepted", "Review", "Bob"), (reply.State, reply.StateModel, reply.Title));
        Assert.Equal("Note", reply.IconName);
        Assert.Null(note.InReplyTo);
        Assert.Null(reply.Popup);

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void Subtype_specific_entries_are_typed()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-subtypes.pdf"));
        IReadOnlyList<PdfAnnotation> annotations = document.Pages[0].Annotations;

        PdfFreeTextAnnotation freeText = Assert.IsType<PdfFreeTextAnnotation>(annotations[4]);
        Assert.Equal(("/Helv 12 Tf 0 g", PdfTextJustification.Centered), (freeText.DefaultAppearance, freeText.Justification));
        Assert.Equal(new CosName("FreeTextCallout"), freeText.Intent);
        Assert.Equal([new PathPoint(10, 10), new PathPoint(50, 50), new PathPoint(60, 50)], freeText.CalloutLine);
        Assert.Equal(PdfLineEnding.OpenArrow, freeText.LineEnding);
        Assert.Equal(("font: 12pt Helvetica", "<p>rich</p>"), (freeText.DefaultStyle, freeText.RichText));

        PdfLineAnnotation line = Assert.IsType<PdfLineAnnotation>(annotations[5]);
        Assert.Equal((new PathPoint(100, 100), new PathPoint(200, 200)), (line.Start, line.End));
        Assert.Equal((PdfLineEnding.Circle, PdfLineEnding.ClosedArrow), (line.StartLineEnding, line.EndLineEnding));
        Assert.Equal(new PdfDeviceColor(1, 0, 0), line.InteriorColor);
        Assert.Equal((10.0, 2.0, 1.0, true), (line.LeaderLineLength, line.LeaderLineExtension, line.LeaderLineOffset, line.HasCaption));
        Assert.Equal((PdfLineCaptionPosition.Top, new PathPoint(0, 5)), (line.CaptionPosition, line.CaptionOffset));
        Assert.Null(line.Measure);

        PdfPolygonAnnotation polygon = Assert.IsType<PdfPolygonAnnotation>(annotations[8]);
        Assert.Equal([new PathPoint(10, 10), new PathPoint(50, 10), new PathPoint(30, 40)], polygon.Vertices);
        Assert.Null(polygon.Path);
        Assert.Equal(2, polygon.BorderEffect!.Intensity);

        PdfPolyLineAnnotation polyLine = Assert.IsType<PdfPolyLineAnnotation>(annotations[9]);
        Assert.Empty(polyLine.Vertices);
        Assert.Equal([[10.0, 10], [50.0, 10], [60.0, 20, 70, 30, 80, 10]], polyLine.Path!);
        Assert.Equal((PdfLineEnding.Square, PdfLineEnding.Slash), (polyLine.StartLineEnding, polyLine.EndLineEnding));

        PdfCaretAnnotation caret = Assert.IsType<PdfCaretAnnotation>(annotations[14]);
        Assert.Equal((PdfCaretSymbol.Paragraph, new PdfRectangleDifferences(1, 1, 1, 1)), (caret.Symbol, caret.RectangleDifferences));

        PdfStampAnnotation stamp = Assert.IsType<PdfStampAnnotation>(annotations[15]);
        Assert.Null(stamp.IconName);
        Assert.Equal(new CosName("StampImage"), stamp.Intent);

        PdfInkAnnotation ink = Assert.IsType<PdfInkAnnotation>(annotations[16]);
        Assert.Equal([3, 2], ink.InkList.Select(path => path.Count));
        Assert.Equal(new PathPoint(50, 50), ink.InkList[1][1]);

        PdfFileAttachmentAnnotation attachment = Assert.IsType<PdfFileAttachmentAnnotation>(annotations[17]);
        Assert.Equal(("Paperclip", "a.txt", "An attached file"), (attachment.IconName, attachment.FileSpecification?.FileName, attachment.Contents));
        Assert.Equal("attached"u8.ToArray(), attachment.FileSpecification!.EmbeddedFile!.Decode().ToArray());
        Assert.Equal("a.txt", Assert.Single(attachment.AssociatedFiles).FileName);

        PdfSoundAnnotation sound = Assert.IsType<PdfSoundAnnotation>(annotations[18]);
        Assert.Equal((4, "Mic"), (sound.Sound!.EncodedData.Length, sound.IconName));

        PdfMovieAnnotation movie = Assert.IsType<PdfMovieAnnotation>(annotations[19]);
        Assert.Equal("Clip", movie.MovieTitle);
        Assert.NotNull(movie.Movie);
        Assert.Equal(CosBoolean.False, movie.Activation);

        PdfScreenAnnotation screen = Assert.IsType<PdfScreenAnnotation>(annotations[20]);
        Assert.Equal("Screen", screen.ScreenTitle);
        Assert.Equal((90, "Play"), (screen.AppearanceCharacteristics!.Rotation, screen.AppearanceCharacteristics.NormalCaption));
        Assert.Equal((new PdfDeviceColor(1, 0, 0), new PdfDeviceColor(1)), (screen.AppearanceCharacteristics.BorderColor, screen.AppearanceCharacteristics.BackgroundColor));
        Assert.Equal("https://example.org/media", Assert.IsType<PdfUriAction>(screen.Action).Uri);

        PdfWidgetAnnotation widget = Assert.IsType<PdfWidgetAnnotation>(annotations[21]);
        Assert.Equal((PdfHighlightMode.Push, "Push", 0), (widget.HighlightMode, widget.AppearanceCharacteristics!.NormalCaption, widget.AppearanceCharacteristics.TextPosition));
        Assert.Null(widget.Parent);
        Assert.Null(widget.Action);

        PdfPrinterMarkAnnotation mark = Assert.IsType<PdfPrinterMarkAnnotation>(annotations[22]);
        Assert.Equal((new CosName("ColorBar"), PdfAnnotationFlags.Print | PdfAnnotationFlags.ReadOnly), (mark.MarkName, mark.Flags));

        PdfWatermarkAnnotation watermark = Assert.IsType<PdfWatermarkAnnotation>(annotations[23]);
        Assert.Equal((new Matrix(1, 0, 0, 1, 72, -72), 0.0, 1.0, true), (watermark.FixedPrint!.Matrix, watermark.FixedPrint.HorizontalTranslation, watermark.FixedPrint.VerticalTranslation, watermark.FixedPrint.HasValidType));

        Pdf3DAnnotation model = Assert.IsType<Pdf3DAnnotation>(annotations[24]);
        Assert.IsType<CosStream>(model.Artwork);
        Assert.Equal((false, new PdfRectangle(0, 0, 60, 40)), (model.IsInteractive, model.ViewBox));
        Assert.Null(model.DefaultView);

        PdfRedactAnnotation redact = Assert.IsType<PdfRedactAnnotation>(annotations[25]);
        Assert.Equal(("X", true, "/Helv 10 Tf 0 g", PdfTextJustification.Right), (redact.OverlayText, redact.RepeatsOverlayText, redact.DefaultAppearance, redact.Justification));
        Assert.Single(redact.QuadPoints);
        Assert.Null(redact.Overlay);

        PdfProjectionAnnotation projection = Assert.IsType<PdfProjectionAnnotation>(annotations[26]);
        Assert.Equal(default, projection.Rect);
        Assert.Null(projection.AppearanceDictionary);

        PdfRichMediaAnnotation richMedia = Assert.IsType<PdfRichMediaAnnotation>(annotations[27]);
        Assert.NotNull(richMedia.Content);
        Assert.NotNull(richMedia.Settings);

        PdfTrapNetAnnotation trapNet = Assert.IsType<PdfTrapNetAnnotation>(annotations[29]);
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), trapNet.LastModified?.Value);
        Assert.Equal(new CosName("T1"), trapNet.AppearanceState);
        Assert.NotNull(trapNet.GetAppearance());

        Assert.Empty(document.Diagnostics);
    }

    [Theory]
    [InlineData("/StateModel (Marked)", "Unmarked")]
    [InlineData("/StateModel (Review)", "None")]
    [InlineData("/StateModel (Custom)", null)]
    [InlineData("", null)]
    [InlineData("/StateModel (Review) /State (Rejected)", "Rejected")]
    public void A_text_annotation_without_State_reads_the_default_of_its_state_model(string entries, string? expected)
    {
        using PdfDocument document = PdfDocument.Open(TestPdf.OnePage($"/MediaBox [0 0 612 792] /Annots [<< /Type /Annot /Subtype /Text /Rect [0 0 10 10] {entries} >>]"));

        PdfTextAnnotation note = Assert.IsType<PdfTextAnnotation>(Assert.Single(document.Pages[0].Annotations));

        Assert.Equal(expected, note.State);
    }
}

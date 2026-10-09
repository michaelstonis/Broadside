using Broadside.Annotations;
using Broadside.Graphics;
using Broadside.Objects;
using Broadside.TestSupport;

namespace Broadside.Tests.Annotations;

/// <summary>Appearance streams (ISO 32000-2 §12.5.5, Table 170) and Algorithm "Appearance streams".</summary>
public class AppearanceTests
{
    public static TheoryData<double[], double[], double[], double[]?> AlgorithmVectors => new()
    {
        // (a) rotated form matrix: T = (-50, 0, 0, 100), A = [1 0 0 1 150 100].
        { [100, 100, 150, 200], [0, 0, 100, 50], [0, 1, -1, 0, 0, 0], [0, 1, -1, 0, 150, 100] },

        // (b) identity matrix, scaled to the rectangle.
        { [10, 10, 50, 30], [0, 0, 20, 20], [1, 0, 0, 1, 0, 0], [2, 0, 0, 1, 10, 10] },

        // (c) the same rectangle written unnormalized (§7.9.5) gives the same matrix.
        { [50, 30, 10, 10], [0, 0, 20, 20], [1, 0, 0, 1, 0, 0], [2, 0, 0, 1, 10, 10] },

        // A bounding box offset from the origin maps its lower-left corner to the rectangle's.
        { [0, 0, 10, 10], [5, 5, 15, 15], [1, 0, 0, 1, 0, 0], [1, 0, 0, 1, -5, -5] },

        // Degenerate: a zero-area bounding box or rectangle cannot be drawn.
        { [10, 10, 50, 30], [0, 0, 0, 0], [1, 0, 0, 1, 0, 0], null },
        { [10, 10, 10, 10], [0, 0, 20, 20], [1, 0, 0, 1, 0, 0], null },
    };

    [Theory]
    [MemberData(nameof(AlgorithmVectors))]
    public void The_appearance_matrix_maps_the_transformed_bounding_box_onto_the_rectangle(double[] rect, double[] bbox, double[] matrix, double[]? expected)
    {
        Matrix? actual = PdfAnnotation.ComputeAppearanceMatrix(
            new PdfRectangle(rect[0], rect[1], rect[2], rect[3]),
            new PdfRectangle(bbox[0], bbox[1], bbox[2], bbox[3]),
            new Matrix(matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5]));

        Assert.Equal(expected is null ? null : new Matrix(expected[0], expected[1], expected[2], expected[3], expected[4], expected[5]), actual);
    }

    [Fact]
    public void A_form_point_lands_where_the_algorithm_puts_it()
    {
        Matrix aa = PdfAnnotation.ComputeAppearanceMatrix(new PdfRectangle(100, 100, 150, 200), new PdfRectangle(0, 0, 100, 50), new Matrix(0, 1, -1, 0, 0, 0))!.Value;

        Assert.Equal(new PathPoint(100, 200), aa.Transform(100, 50));
        Assert.Equal(new PathPoint(150, 100), aa.Transform(0, 0));
    }

    [Fact]
    public void Annotations_appearance_pdf_places_each_appearance_on_its_rectangle()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-appearance.pdf"));
        IReadOnlyList<PdfAnnotation> annotations = document.Pages[0].Annotations;

        PdfFormXObject rotated = annotations[0].GetAppearance()!;
        Assert.Equal((new PdfRectangle(0, 0, 100, 50), new Matrix(0, 1, -1, 0, 0, 0)), (rotated.BoundingBox, rotated.Matrix));
        Assert.Equal(new Matrix(0, 1, -1, 0, 150, 100), annotations[0].GetAppearanceMatrix(rotated));
        Assert.Equal("1 0 0 rg 0 0 100 50 re f"u8.ToArray(), rotated.Decode().ToArray());
        Assert.True(rotated.IsForm);

        PdfAnnotation circle = annotations[1];
        Assert.Equal(new PdfRectangle(10, 10, 50, 30), circle.Rect);
        Assert.Equal(new Matrix(2, 0, 0, 1, 10, 10), circle.GetAppearanceMatrix(circle.GetAppearance()!));

        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void States_select_the_appearance_and_missing_entries_fall_back_to_the_normal_one()
    {
        using PdfDocument document = PdfDocument.Open(Corpus.Path("annotations-appearance.pdf"));
        PdfAnnotation square = document.Pages[0].Annotations[2];
        PdfAppearanceDictionary appearance = square.AppearanceDictionary!;
        var on = new CosName("On");
        var off = new CosName("Off");

        Assert.Equal(on, square.AppearanceState);
        Assert.True(appearance.Normal!.HasStates);
        Assert.Null(appearance.Normal.Form);
        Assert.Equal([on, off], appearance.Normal.StateNames);
        Assert.False(appearance.HasRollover);
        Assert.True(appearance.HasDown);

        Assert.Equal(new CosReference(6, 0), square.GetAppearance()!.Reference);
        Assert.Equal(new CosReference(6, 0), square.GetAppearance(PdfAppearanceMode.Rollover)!.Reference);
        Assert.Equal(new CosReference(8, 0), square.GetAppearance(PdfAppearanceMode.Down)!.Reference);
        Assert.Equal(new CosReference(7, 0), square.GetAppearance(PdfAppearanceMode.Normal, off)!.Reference);
        Assert.Equal(new CosReference(7, 0), square.GetAppearance(PdfAppearanceMode.Down, off)!.Reference);
        Assert.Null(square.GetAppearance(PdfAppearanceMode.Normal, new CosName("Missing")));

        Assert.Empty(document.Diagnostics);
    }
}

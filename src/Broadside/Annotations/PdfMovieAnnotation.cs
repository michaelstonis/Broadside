using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>A movie annotation: a movie to be played. Deprecated in PDF 2.0.</summary>
/// <remarks>ISO 32000-2 §12.5.6.17, Table 189 (PDF 1.2). Parse-and-preserve: the movie and its activation are exposed as data and never played.</remarks>
public sealed class PdfMovieAnnotation : PdfAnnotation
{
    internal PdfMovieAnnotation(PdfDocument document, CosDictionary dictionary, CosReference? reference, PdfPage? page)
        : base(document, dictionary, reference, page, PdfAnnotationKind.Movie)
    {
    }

    /// <summary>Gets the title of the annotation (<c>T</c>), used by movie actions to name it, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.17, Table 189.</remarks>
    public string? MovieTitle => ReadText(AnnotationNames.T);

    /// <summary>Gets the movie dictionary (<c>Movie</c>, required), as stored, or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.17, Table 189, and §13.4.</remarks>
    public CosDictionary? Movie
    {
        get
        {
            if (!Dictionary.ContainsKey(AnnotationNames.Movie))
            {
                ReportMissing(AnnotationNames.Movie, "Table 189");
            }

            return ReadDictionary(AnnotationNames.Movie);
        }
    }

    /// <summary>Gets the activation entry (<c>A</c>) as stored: a boolean saying whether to play the movie when activated, or a movie activation dictionary; <see langword="null"/> when absent (play, the default). Not an action.</summary>
    /// <remarks>ISO 32000-2 §12.5.6.17, Table 189, and §13.4, Table 307.</remarks>
    public CosObject? Activation => Get(AnnotationNames.A);
}

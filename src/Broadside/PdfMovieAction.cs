using Broadside.Objects;

namespace Broadside;

/// <summary>A movie action: plays, stops, pauses or resumes a movie. Kept as data; the library never plays anything.</summary>
/// <remarks>
/// ISO 32000-2 §12.6.4.10, Table 213 (PDF 1.2; deprecated in PDF 2.0). The dictionary may also hold the entries of a movie activation
/// dictionary (Table 307: Start, Duration, Rate, Volume, ShowControls, Mode, Synchronous, FWScale, FWPosition), which override those of
/// the movie annotation; they are reachable through <see cref="PdfAction.Dictionary"/>.
/// </remarks>
public sealed class PdfMovieAction : PdfAction
{
    internal PdfMovieAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.Movie;

    /// <summary>Gets the movie annotation whose movie to play (<c>Annotation</c>, an indirect reference), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.10, Table 213. Either this or <see cref="Title"/> identifies the movie, not both.</remarks>
    public CosDictionary? Annotation => ReadDictionary(ActionNames.Annotation);

    /// <summary>Gets the title of the movie annotation whose movie to play (<c>T</c>), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.10, Table 213.</remarks>
    public string? Title => ReadText(ActionNames.T);

    /// <summary>Gets the operation (<c>Operation</c>); <see cref="PdfMovieOperation.Play"/> when absent or unknown (an unknown name with a diagnostic).</summary>
    /// <remarks>ISO 32000-2 §12.6.4.10, Table 213.</remarks>
    public PdfMovieOperation Operation
    {
        get
        {
            switch (ReadName(ActionNames.Operation)?.Value)
            {
                case null or "Play":
                    return PdfMovieOperation.Play;
                case "Stop":
                    return PdfMovieOperation.Stop;
                case "Pause":
                    return PdfMovieOperation.Pause;
                case "Resume":
                    return PdfMovieOperation.Resume;
                default:
                    ReportEntry(ActionNames.Operation, "Play, Stop, Pause or Resume");
                    return PdfMovieOperation.Play;
            }
        }
    }

    /// <inheritdoc/>
    private protected override void Check()
    {
        ReportOutOfScope("movie playback");
        bool annotation = Dictionary.ContainsKey(ActionNames.Annotation);
        bool title = Dictionary.ContainsKey(ActionNames.T);
        if (annotation == title)
        {
            Report("A movie action shall have either an Annotation or a T entry, but not both.");
        }
    }
}

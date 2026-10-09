using Broadside.Objects;

namespace Broadside;

/// <summary>A sound action: plays a sound. Kept as data; the library never plays anything.</summary>
/// <remarks>ISO 32000-2 §12.6.4.9, Table 212 (PDF 1.2; deprecated in PDF 2.0), and §13.3 (sound objects, exposed raw).</remarks>
public sealed class PdfSoundAction : PdfAction
{
    internal PdfSoundAction(PdfDocument document, CosDictionary dictionary, CosReference? reference, CosName actionType)
        : base(document, dictionary, reference, actionType)
    {
    }

    /// <inheritdoc/>
    public override PdfActionKind Kind => PdfActionKind.Sound;

    /// <summary>Gets the sound object (<c>Sound</c>, required), or <see langword="null"/>.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.9, Table 212, and §13.3, Table 302.</remarks>
    public CosStream? Sound => ReadStream(ActionNames.Sound);

    /// <summary>Gets the volume (<c>Volume</c>), -1.0 to 1.0; 1.0 when absent. A value outside the range is clamped into it, with a diagnostic.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.9, Table 212.</remarks>
    public double Volume
    {
        get
        {
            double volume = ReadNumber(ActionNames.Volume) ?? 1.0;
            if (volume is >= -1.0 and <= 1.0)
            {
                return volume;
            }

            ReportEntry(ActionNames.Volume, "a number from -1.0 to 1.0");
            return Math.Clamp(volume, -1.0, 1.0);
        }
    }

    /// <summary>Gets whether the sound plays synchronously (<c>Synchronous</c>); <see langword="false"/> when absent. Ignored when <c>Repeat</c> is present.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.9, Table 212.</remarks>
    public bool IsSynchronous => ReadBoolean(ActionNames.Synchronous) ?? false;

    /// <summary>Gets whether the sound repeats indefinitely (<c>Repeat</c>); <see langword="false"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.9, Table 212.</remarks>
    public bool Repeats => ReadBoolean(ActionNames.Repeat) ?? false;

    /// <summary>Gets whether the sound mixes with any sound already playing (<c>Mix</c>); <see langword="false"/> when absent.</summary>
    /// <remarks>ISO 32000-2 §12.6.4.9, Table 212.</remarks>
    public bool Mixes => ReadBoolean(ActionNames.Mix) ?? false;

    /// <inheritdoc/>
    private protected override void Check()
    {
        ReportOutOfScope("sound playback");
        if (!Dictionary.ContainsKey(ActionNames.Sound))
        {
            Report("A sound action shall have a Sound entry; it has none.");
        }
    }
}

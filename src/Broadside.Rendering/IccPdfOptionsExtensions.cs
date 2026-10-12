using Broadside.Rendering.Icc;

// The extension lives in the options' namespace so UseIccColorManagement is found next to the other Use* calls without another using.
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Broadside;
#pragma warning restore IDE0130

/// <summary>Wires the managed ICC engine into an engine's options.</summary>
/// <remarks>ISO 32000-2 §8.6.5.5 and §10.3.</remarks>
public static class IccPdfOptionsExtensions
{
    /// <summary>
    /// Converts ICCBased colours through their embedded profiles: registers an <see cref="IccColorManagement"/> wrapping
    /// <see cref="Graphics.ManagedColorManagement.Default"/> with <see cref="PdfOptions.UseColorManagement"/>.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>These options.</returns>
    /// <remarks>
    /// ISO 32000-2 §8.6.5.5. To wrap another colour management, pass <c>new IccColorManagement(other)</c> to
    /// <see cref="PdfOptions.UseColorManagement"/> instead.
    /// </remarks>
    public static PdfOptions UseIccColorManagement(this PdfOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.UseColorManagement(new IccColorManagement());
    }
}

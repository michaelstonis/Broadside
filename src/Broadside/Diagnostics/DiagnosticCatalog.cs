using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Broadside.Diagnostics;

/// <summary>Every <see cref="DiagnosticCode"/> Broadside reports, across the catalog classes of all areas.</summary>
/// <remarks>
/// <para>
/// One catalog class lists the codes of one <see cref="DiagnosticArea"/>, such as <see cref="FileStructureDiagnostics"/> or
/// <see cref="FontDiagnostics"/>. Names are unique across areas and never change; a code that is no longer reported is removed, never
/// renamed.
/// </para>
/// <para>
/// Recording a repair as a diagnostic instead of throwing is how Broadside reads damaged files; ISO 32000-2 §7.5 describes the file
/// structure a reader repairs but does not specify the repair.
/// </para>
/// </remarks>
public static class DiagnosticCatalog
{
    /// <summary>Gets every code, area by area in the order of <see cref="DiagnosticArea"/>, each area in declaration order.</summary>
    public static IReadOnlyList<DiagnosticCode> All => Index.All;

    /// <summary>Finds the code with a name.</summary>
    /// <param name="name">The name, such as <c>StartxrefMissing</c>; matched ordinally.</param>
    /// <param name="code">The code, or <see langword="null"/> when the catalog has none of that name.</param>
    /// <returns><see langword="true"/> when the catalog has a code of that name.</returns>
    public static bool TryGet(string name, [NotNullWhen(true)] out DiagnosticCode? code)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Index.ByName.TryGetValue(name, out code);
    }

    /// <summary>
    /// Gets the catalog's code of a name: the bridge for internal helpers that still pass a code's name to a typed report method
    /// (until their area's report sites take <see cref="DiagnosticCode"/> themselves).
    /// </summary>
    /// <param name="name">The name of a code of the catalog.</param>
    /// <returns>The code.</returns>
    /// <exception cref="ArgumentException">The catalog has no code of that name: a library bug.</exception>
    internal static DiagnosticCode Get(string name) =>
        TryGet(name, out DiagnosticCode? code) ? code : throw new ArgumentException($"{name} is not a code of the diagnostic catalog.", nameof(name));

    // Built on first use, so reporting a diagnostic never pays for the lookup table.
    private static class Index
    {
        public static readonly DiagnosticCode[] All =
        [
            .. FileStructureDiagnostics.Codes,
            .. ObjectSyntaxDiagnostics.Codes,
            .. SecurityDiagnostics.Codes,
            .. DocumentDiagnostics.Codes,
            .. PageDiagnostics.Codes,
            .. SavingDiagnostics.Codes,
            .. NavigationDiagnostics.Codes,
            .. OptionalContentDiagnostics.Codes,
            .. FileSpecificationDiagnostics.Codes,
            .. FilterDiagnostics.Codes,
            .. DctDiagnostics.Codes,
            .. CcittDiagnostics.Codes,
            .. Jbig2Diagnostics.Codes,
            .. JpxDiagnostics.Codes,
            .. ImageDiagnostics.Codes,
            .. FontDiagnostics.Codes,
            .. CharStringDiagnostics.Codes,
            .. CffDiagnostics.Codes,
            .. Type1Diagnostics.Codes,
            .. Type3Diagnostics.Codes,
            .. CMapDiagnostics.Codes,
            .. TextDiagnostics.Codes,
            .. ContentDiagnostics.Codes,
            .. FunctionDiagnostics.Codes,
            .. ColorDiagnostics.Codes,
            .. ShadingDiagnostics.Codes,
            .. ActionDiagnostics.Codes,
            .. AnnotationDiagnostics.Codes,
            .. FormDiagnostics.Codes,
            .. StructureDiagnostics.Codes,
        ];

        public static readonly FrozenDictionary<string, DiagnosticCode> ByName = All.ToFrozenDictionary(code => code.Name, StringComparer.Ordinal);
    }
}

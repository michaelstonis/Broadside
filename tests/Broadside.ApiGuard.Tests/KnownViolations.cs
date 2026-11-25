namespace Broadside.ApiGuard.Tests;

/// <summary>
/// The breaches of each mechanical rule the v1 API review (#262) accepted, kept visible instead of silenced: every entry names
/// the member and why it is allowed. The guards fail on a breach that is not listed here and on an entry that no longer breaches
/// its rule, so this list only shrinks: removing an entry is part of fixing it, and adding one needs a reason a reviewer accepts.
/// The accepted exceptions are also listed in the review checklist (site/articles/api-review.md, "Accepted exceptions"). Keys are
/// what the guard prints (<see cref="ApiSurface.Describe"/>, or the PublicAPI line for the platform packages).
/// </summary>
internal static class KnownViolations
{
    private const string Accumulator =
        "an accumulator: one call per certificate adds an entry, which neither Use* (select an implementation) nor With* (set a value) describes";

    private const string AbstractExtensionPoint =
        "an abstract base class, not an interface, so that it can gain members in 1.x without breaking the classes derived from it";

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> ByRule = new(StringComparer.Ordinal)
    {
        [nameof(ApiRules.UseAndWith)] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Broadside.PdfPublicKeyEncryptionOptions.AddOwner(X509Certificate2)"] = Accumulator,
            ["Broadside.PdfPublicKeyEncryptionOptions.AddRecipient(X509Certificate2, PdfPermissions)"] = Accumulator,
        },

        [nameof(ApiRules.ExtensionPointsAreInterfaces)] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Content processing: a consumer derives from these to receive content events or rewrite a content stream.
            ["Broadside.Content.ContentEditor"] = AbstractExtensionPoint,
            ["Broadside.Content.ContentProcessor"] = AbstractExtensionPoint,

            // What a font program parser (IFontProgramParser, an interface) returns: a third-party parser derives from them.
            ["Broadside.Fonts.FontCharacterMap"] = AbstractExtensionPoint,
            ["Broadside.Fonts.FontProgram"] = AbstractExtensionPoint,

            // The credentials a custom security handler (ISecurityHandler, an interface) accepts derive from this base.
            ["Broadside.Security.PdfCredentials"] = AbstractExtensionPoint,
        },

        [nameof(ApiRules.GlossaryWords)] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The rule's avoided nouns are synonyms the glossary rejects elsewhere; these two names are glossary terms themselves.
            ["Broadside.Viewer.IReadingOrderProvider"] = "the glossary term \"Reading-order provider\" (CONTEXT.md, Viewer)",
            ["Broadside.Viewer.IRemoteTileRenderer"] = "the glossary term \"Remote tile renderer\" (CONTEXT.md, Viewer)",
        },

        [nameof(ApiRules.ExtensionPointsHaveManagedDefaults)] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Broadside.Rendering.IRenderingBackend"] =
                "a backend is handed to the replay call (DisplayList.Replay) instead of being registered on an options type: a display "
                + "list is replayed into whichever backend draws it, and the software rasterizer is the managed default",
        },
    };

    /// <summary>The rule name of the PublicAPI-file checks over the platform packages.</summary>
    public const string PlatformSignatures = "Platform package signatures";

    /// <summary>The listed breaches of a rule (none for a rule with no entry).</summary>
    public static IReadOnlyDictionary<string, string> For(string rule) =>
        ByRule.TryGetValue(rule, out IReadOnlyDictionary<string, string>? known) ? known : new Dictionary<string, string>();
}

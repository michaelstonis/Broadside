namespace Broadside.ApiGuard.Tests;

/// <summary>
/// The current breaches of each mechanical rule, kept visible instead of silenced: every entry names the member and why it is
/// listed. They were found when the guards landed (#261) and are decided, fixed or accepted, in the package-by-package API
/// review before the v1 freeze (#262). The guards fail on a breach that is not listed here and on an entry that no longer
/// breaches its rule, so this list only shrinks: removing an entry is part of fixing it, and adding one needs a reason a reviewer
/// accepts. Keys are what the guard prints (<see cref="ApiSurface.Describe"/>, or the PublicAPI line for the platform backends).
/// </summary>
internal static class KnownViolations
{
    private const string Review = "decided in the v1 API review (#262)";

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> ByRule = new(StringComparer.Ordinal)
    {
        [nameof(ApiRules.UseAndWith)] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // A logger factory is an implementation the engine logs through: UseLoggerFactory by the rule; the hosting guard
            // (OptionsGuard) already treats it as an option that cannot bind from configuration. Review.
            ["Broadside.PdfOptions.WithLoggerFactory(ILoggerFactory)"] = Review,

            // Recipients accumulate (one call per certificate), which neither Use* (select) nor With* (set) describes. Review.
            ["Broadside.PdfPublicKeyEncryptionOptions.AddOwner(X509Certificate2)"] = Review,
            ["Broadside.PdfPublicKeyEncryptionOptions.AddRecipient(X509Certificate2, PdfPermissions)"] = Review,
        },

        [nameof(ApiRules.CancellationTokenLast)] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The progress callback follows the token on the replay and render entry points of the rendering engine. Review.
            ["Broadside.Rendering.DisplayList.BeginReplay(IRenderingBackend, CancellationToken, IProgress<Double>)"] = Review,
            ["Broadside.Rendering.DisplayList.BeginReplay(IRenderingBackend, PdfRectangle, CancellationToken, IProgress<Double>)"] = Review,
            ["Broadside.Rendering.DisplayList.Replay(IRenderingBackend, CancellationToken, IProgress<Double>)"] = Review,
            ["Broadside.Rendering.SoftwareRasterizer.BeginRender(DisplayList, Bitmap, Matrix&, CancellationToken, IProgress<Double>)"] = Review,
        },

        [nameof(ApiRules.AsyncNaming)] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Loads the backend's JavaScript module and creates the WebGL context; it takes no cancellation token. Review.
            ["Broadside.Rendering.WebGL.WebGLRenderer.InitializeAsync()"] = Review,
        },

        [nameof(ApiRules.NoOptionalParametersOnOverloads)] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Each has a defaulted parameter beside another overload of the same name (the open and save entry points were
            // settled by #250; these were not in its scope). Review.
            ["Broadside.Layout.Flow.TableElement.Border(Length, PdfColor?)"] = Review,
            ["Broadside.PdfDocument.AddEmbeddedFile(String, ReadOnlySpan<Byte>, PdfEmbeddedFileOptions)"] = Review,
            ["Broadside.PdfDocument.EmbedFont(ReadOnlyMemory<Byte>, Int32)"] = Review,
            ["Broadside.PdfDocument.EmbedTrueTypeFont(ReadOnlyMemory<Byte>, Int32)"] = Review,
            ["Broadside.PdfDocument.GetColorConverter(PdfColorSpace, ColorConversion, PdfDefaultColorSpaces)"] = Review,
            ["Broadside.PdfEngine.PdfEngine(IOptions<PdfOptions>, ILoggerFactory)"] = Review,
            ["Broadside.PdfPage.CreateHitTestIndex(DisplayList, PdfOptionalContentState)"] = Review,
            ["Broadside.Rendering.PdfImageRenderingExtensions.ToBitmap(PdfImage, PdfColor, PdfDefaultColorSpaces)"] = Review,
        },

        [nameof(ApiRules.ExtensionPointsAreInterfaces)] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Content processing: a consumer derives from these to receive content events or rewrite a content stream (the
            // content-processing seam, with CompositeContentProcessor and ImageExtractor as shipped implementations). Review.
            ["Broadside.Content.ContentEditor"] = Review,
            ["Broadside.Content.ContentProcessor"] = Review,

            // What a font program parser (IFontProgramParser, an interface) returns: a third-party parser derives from them.
            ["Broadside.Fonts.FontCharacterMap"] = Review,
            ["Broadside.Fonts.FontProgram"] = Review,

            // The credentials a custom security handler (ISecurityHandler, an interface) accepts derive from this base. Review.
            ["Broadside.Security.PdfCredentials"] = Review,
        },

        [nameof(ApiRules.ExtensionPointsHaveManagedDefaults)] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // A backend is handed to the replay call (DisplayList.Replay) instead of being registered on an options type; the spec
            // lists backends among the extension points registered through the options object. Review.
            ["Broadside.Rendering.IRenderingBackend"] = Review,
        },

        [nameof(ApiRules.GlossaryWords)] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Writes the fields' appearance streams; the glossary avoids "generate" for Write. Review.
            ["Broadside.Forms.PdfChoiceField.GenerateAppearances()"] = Review,
            ["Broadside.Forms.PdfTextField.GenerateAppearances()"] = Review,
            ["Broadside.Forms.PdfToggleButtonField.GenerateAppearances()"] = Review,

            // Backends named "Renderer"/"Driver", which the glossary avoids for Backend (SkiaBackend and AndroidCanvasBackend
            // follow it). Review.
            ["Broadside.Rendering.WebGL.WebGLRenderer"] = Review,
        },

        [PlatformSignatures] = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // As GlossaryWords: native backends named "Renderer"/"Driver". Review.
            ["Broadside.Rendering.CoreGraphics.CoreGraphicsRenderer"] = Review,
            ["Broadside.Rendering.Direct2D.Direct2DDriver"] = Review,
            ["Broadside.Rendering.Direct2D.Direct2DRenderer"] = Review,
        },
    };

    /// <summary>The rule name of the PublicAPI-file checks over the platform backends.</summary>
    public const string PlatformSignatures = "Platform backend signatures";

    /// <summary>The listed breaches of a rule (none for a rule with no entry).</summary>
    public static IReadOnlyDictionary<string, string> For(string rule) =>
        ByRule.TryGetValue(rule, out IReadOnlyDictionary<string, string>? known) ? known : new Dictionary<string, string>();
}

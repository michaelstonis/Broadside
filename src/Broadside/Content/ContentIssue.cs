using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Content;

/// <summary>The deviations the content interpreter records, each mapped to one diagnostic code and severity.</summary>
/// <remarks>
/// Information for deviations that do not change what is painted (Figure 9 context rules, glued tokens, a path never painted)
/// and for operators the specification itself says to ignore (colour operators in a <c>d1</c> glyph or an uncoloured tiling
/// pattern, §8.6.8); Warning for operators skipped or repaired. Each is recorded at most once per run, so malformed content costs one diagnostic per
/// kind, not one per operator.
/// </remarks>
internal enum ContentIssue
{
    StreamInvalid,
    UnknownOperator,
    OperandCount,
    OperandType,
    OperandOverflow,
    SyntaxInvalid,
    GluedTokens,
    OperatorOutOfContext,
    StackUnderflow,
    StackOverflow,
    UnbalancedSave,
    NoCurrentPoint,
    NoCurrentPath,
    PathNotPainted,
    GraphicsStateRange,
    CompatibilityUnbalanced,
    TextObjectUnbalanced,
    InlineImageInvalid,
    ColorSpaceMissing,
    ColorSpaceAbbreviated,
    ColorOperandCount,
    ColorOperatorMismatch,
    ColorOperatorInvalid,
    ColorOperatorIgnored,
    PatternMissing,
    ColorComponentLimit,
    FontMissing,
    ExtGStateMissing,
    ExtGStateInvalid,
    XObjectMissing,
    XObjectInvalid,
    PostScriptXObject,
    FormInvalid,
    FormCycle,
    NestingTooDeep,
    InlineImageLengthMissing,
    PropertiesMissing,
    MarkedContentUnbalanced,
    ShadingMissing,
    PatternRecursion,
    Type3GlyphMetricsMissing,
    Type3GlyphMetricsMisplaced,
    Type3GlyphRecursion,
    AppearanceResourcesMissing,
}

/// <summary>The code and severity of each <see cref="ContentIssue"/>.</summary>
internal static class ContentIssues
{
    public static DiagnosticCode Code(ContentIssue issue) => issue switch
    {
        ContentIssue.StreamInvalid => ContentDiagnostics.ContentStreamInvalid,
        ContentIssue.UnknownOperator => ContentDiagnostics.ContentUnknownOperator,
        ContentIssue.OperandCount => ContentDiagnostics.ContentOperandCount,
        ContentIssue.OperandType => ContentDiagnostics.ContentOperandType,
        ContentIssue.OperandOverflow => ContentDiagnostics.ContentOperandOverflow,
        ContentIssue.SyntaxInvalid => ContentDiagnostics.ContentSyntaxInvalid,
        ContentIssue.GluedTokens => ContentDiagnostics.ContentGluedTokens,
        ContentIssue.OperatorOutOfContext => ContentDiagnostics.ContentOperatorOutOfContext,
        ContentIssue.StackUnderflow => ContentDiagnostics.ContentStackUnderflow,
        ContentIssue.StackOverflow => ContentDiagnostics.ContentStackOverflow,
        ContentIssue.UnbalancedSave => ContentDiagnostics.ContentUnbalancedSave,
        ContentIssue.NoCurrentPoint => ContentDiagnostics.ContentNoCurrentPoint,
        ContentIssue.NoCurrentPath => ContentDiagnostics.ContentNoCurrentPath,
        ContentIssue.PathNotPainted => ContentDiagnostics.ContentPathNotPainted,
        ContentIssue.GraphicsStateRange => ContentDiagnostics.ContentGraphicsStateRange,
        ContentIssue.CompatibilityUnbalanced => ContentDiagnostics.ContentCompatibilityUnbalanced,
        ContentIssue.TextObjectUnbalanced => ContentDiagnostics.ContentTextObjectUnbalanced,
        ContentIssue.ColorSpaceMissing => ColorDiagnostics.ContentColorSpaceMissing,
        ContentIssue.ColorSpaceAbbreviated => ColorDiagnostics.ContentColorSpaceAbbreviated,
        ContentIssue.ColorOperandCount => ColorDiagnostics.ContentColorOperandCount,
        ContentIssue.ColorOperatorMismatch => ColorDiagnostics.ContentColorOperatorMismatch,
        ContentIssue.ColorOperatorInvalid => ColorDiagnostics.ContentColorOperatorInvalid,
        ContentIssue.ColorOperatorIgnored => ColorDiagnostics.ContentColorOperatorIgnored,
        ContentIssue.PatternMissing => ColorDiagnostics.ContentPatternMissing,
        ContentIssue.ColorComponentLimit => ColorDiagnostics.ColorComponentLimitExceeded,
        ContentIssue.FontMissing => ContentDiagnostics.ContentFontMissing,
        ContentIssue.ExtGStateMissing => ContentDiagnostics.ContentExtGStateMissing,
        ContentIssue.ExtGStateInvalid => ContentDiagnostics.ContentExtGStateInvalid,
        ContentIssue.XObjectMissing => ContentDiagnostics.ContentXObjectMissing,
        ContentIssue.XObjectInvalid => ContentDiagnostics.ContentXObjectInvalid,
        ContentIssue.PostScriptXObject => ContentDiagnostics.ContentPostScriptXObject,
        ContentIssue.FormInvalid => ContentDiagnostics.ContentFormInvalid,
        ContentIssue.FormCycle => ContentDiagnostics.ContentFormCycle,
        ContentIssue.NestingTooDeep => ShadingDiagnostics.ContentNestingTooDeep,
        ContentIssue.InlineImageLengthMissing => ContentDiagnostics.ContentInlineImageLengthMissing,
        ContentIssue.PropertiesMissing => ContentDiagnostics.ContentPropertiesMissing,
        ContentIssue.MarkedContentUnbalanced => ContentDiagnostics.ContentMarkedContentUnbalanced,
        ContentIssue.ShadingMissing => ShadingDiagnostics.ContentShadingMissing,
        ContentIssue.PatternRecursion => ShadingDiagnostics.ContentPatternRecursion,
        ContentIssue.Type3GlyphMetricsMissing => Type3Diagnostics.ContentType3GlyphMetricsMissing,
        ContentIssue.Type3GlyphMetricsMisplaced => Type3Diagnostics.ContentType3GlyphMetricsMisplaced,
        ContentIssue.Type3GlyphRecursion => Type3Diagnostics.ContentType3GlyphRecursion,
        ContentIssue.AppearanceResourcesMissing => AnnotationDiagnostics.AppearanceResourcesMissing,
        _ => ContentDiagnostics.ContentInlineImageInvalid,
    };

    public static DiagnosticSeverity Severity(ContentIssue issue) => issue switch
    {
        ContentIssue.GluedTokens or ContentIssue.OperatorOutOfContext or ContentIssue.PathNotPainted or ContentIssue.NoCurrentPath or ContentIssue.ColorComponentLimit
            or ContentIssue.PostScriptXObject or ContentIssue.InlineImageLengthMissing or ContentIssue.ColorOperatorIgnored
            => DiagnosticSeverity.Information,
        _ => DiagnosticSeverity.Warning,
    };
}

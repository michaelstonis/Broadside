using Broadside.Diagnostics;
using Broadside.Parsing;

namespace Broadside.Content;

/// <summary>The deviations the content interpreter records, each mapped to one diagnostic code and severity.</summary>
/// <remarks>
/// Information for deviations that do not change what is painted (Figure 9 context rules, glued tokens, a path never painted);
/// Warning for operators skipped or repaired. Each is recorded at most once per run, so malformed content costs one diagnostic per
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
}

/// <summary>The code and severity of each <see cref="ContentIssue"/>.</summary>
internal static class ContentIssues
{
    public static string Code(ContentIssue issue) => issue switch
    {
        ContentIssue.StreamInvalid => DiagnosticCodes.ContentStreamInvalid,
        ContentIssue.UnknownOperator => DiagnosticCodes.ContentUnknownOperator,
        ContentIssue.OperandCount => DiagnosticCodes.ContentOperandCount,
        ContentIssue.OperandType => DiagnosticCodes.ContentOperandType,
        ContentIssue.OperandOverflow => DiagnosticCodes.ContentOperandOverflow,
        ContentIssue.SyntaxInvalid => DiagnosticCodes.ContentSyntaxInvalid,
        ContentIssue.GluedTokens => DiagnosticCodes.ContentGluedTokens,
        ContentIssue.OperatorOutOfContext => DiagnosticCodes.ContentOperatorOutOfContext,
        ContentIssue.StackUnderflow => DiagnosticCodes.ContentStackUnderflow,
        ContentIssue.StackOverflow => DiagnosticCodes.ContentStackOverflow,
        ContentIssue.UnbalancedSave => DiagnosticCodes.ContentUnbalancedSave,
        ContentIssue.NoCurrentPoint => DiagnosticCodes.ContentNoCurrentPoint,
        ContentIssue.NoCurrentPath => DiagnosticCodes.ContentNoCurrentPath,
        ContentIssue.PathNotPainted => DiagnosticCodes.ContentPathNotPainted,
        ContentIssue.GraphicsStateRange => DiagnosticCodes.ContentGraphicsStateRange,
        ContentIssue.CompatibilityUnbalanced => DiagnosticCodes.ContentCompatibilityUnbalanced,
        ContentIssue.TextObjectUnbalanced => DiagnosticCodes.ContentTextObjectUnbalanced,
        ContentIssue.ColorSpaceMissing => DiagnosticCodes.ContentColorSpaceMissing,
        ContentIssue.ColorSpaceAbbreviated => DiagnosticCodes.ContentColorSpaceAbbreviated,
        ContentIssue.ColorOperandCount => DiagnosticCodes.ContentColorOperandCount,
        ContentIssue.ColorOperatorMismatch => DiagnosticCodes.ContentColorOperatorMismatch,
        ContentIssue.ColorOperatorInvalid => DiagnosticCodes.ContentColorOperatorInvalid,
        ContentIssue.ColorOperatorIgnored => DiagnosticCodes.ContentColorOperatorIgnored,
        ContentIssue.PatternMissing => DiagnosticCodes.ContentPatternMissing,
        ContentIssue.ColorComponentLimit => DiagnosticCodes.ColorComponentLimitExceeded,
        ContentIssue.FontMissing => DiagnosticCodes.ContentFontMissing,
        ContentIssue.ExtGStateMissing => DiagnosticCodes.ContentExtGStateMissing,
        ContentIssue.ExtGStateInvalid => DiagnosticCodes.ContentExtGStateInvalid,
        ContentIssue.XObjectMissing => DiagnosticCodes.ContentXObjectMissing,
        ContentIssue.XObjectInvalid => DiagnosticCodes.ContentXObjectInvalid,
        ContentIssue.PostScriptXObject => DiagnosticCodes.ContentPostScriptXObject,
        ContentIssue.FormInvalid => DiagnosticCodes.ContentFormInvalid,
        ContentIssue.FormCycle => DiagnosticCodes.ContentFormCycle,
        ContentIssue.NestingTooDeep => DiagnosticCodes.ContentNestingTooDeep,
        ContentIssue.InlineImageLengthMissing => DiagnosticCodes.ContentInlineImageLengthMissing,
        ContentIssue.PropertiesMissing => DiagnosticCodes.ContentPropertiesMissing,
        ContentIssue.MarkedContentUnbalanced => DiagnosticCodes.ContentMarkedContentUnbalanced,
        ContentIssue.ShadingMissing => DiagnosticCodes.ContentShadingMissing,
        ContentIssue.PatternRecursion => DiagnosticCodes.ContentPatternRecursion,
        ContentIssue.Type3GlyphMetricsMissing => DiagnosticCodes.ContentType3GlyphMetricsMissing,
        ContentIssue.Type3GlyphMetricsMisplaced => DiagnosticCodes.ContentType3GlyphMetricsMisplaced,
        ContentIssue.Type3GlyphRecursion => DiagnosticCodes.ContentType3GlyphRecursion,
        _ => DiagnosticCodes.ContentInlineImageInvalid,
    };

    public static DiagnosticSeverity Severity(ContentIssue issue) => issue switch
    {
        ContentIssue.GluedTokens or ContentIssue.OperatorOutOfContext or ContentIssue.PathNotPainted or ContentIssue.ColorComponentLimit
            or ContentIssue.PostScriptXObject or ContentIssue.InlineImageLengthMissing
            => DiagnosticSeverity.Information,
        _ => DiagnosticSeverity.Warning,
    };
}

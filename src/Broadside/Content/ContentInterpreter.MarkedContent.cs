namespace Broadside.Content;

/// <summary>The marked-content operators of Table 352: <c>MP DP BMC BDC EMC</c>.</summary>
/// <remarks>
/// ISO 32000-2 §14.6. Operands are checked by the core; sequences, property lists and their events, and optional-content visibility
/// (§8.11.3.1), come with issues #56 and #76.
/// </remarks>
internal sealed partial class ContentInterpreter
{
    private static void ExecuteMarkedContent(ContentOperatorCode code, ContentOperands operands, int offset)
    {
        _ = code;
        _ = operands;
        _ = offset;
    }
}

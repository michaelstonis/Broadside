namespace Broadside.Content;

/// <summary>The colour operators of Table 73: <c>CS cs SC SCN sc scn G g RG rg K k</c>.</summary>
/// <remarks>
/// ISO 32000-2 §8.6.8. Operands are checked by the core; colour spaces and colour values are resolved into the graphics state from
/// issue #77.
/// </remarks>
internal sealed partial class ContentInterpreter
{
    private static void ExecuteColor(ContentOperatorCode code, ContentOperands operands, int offset)
    {
        _ = code;
        _ = operands;
        _ = offset;
    }
}

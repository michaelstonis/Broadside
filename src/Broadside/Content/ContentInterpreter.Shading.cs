namespace Broadside.Content;

/// <summary>The shading operator of Table 76: <c>sh</c>.</summary>
/// <remarks>ISO 32000-2 §8.7.4.2. Its operand is checked by the core; shadings are resolved and reported from issue #79.</remarks>
internal sealed partial class ContentInterpreter
{
    private static void ExecuteShading(ContentOperatorCode code, ContentOperands operands, int offset)
    {
        _ = code;
        _ = operands;
        _ = offset;
    }
}

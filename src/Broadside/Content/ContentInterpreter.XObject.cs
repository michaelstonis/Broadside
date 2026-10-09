namespace Broadside.Content;

/// <summary>XObjects (Table 86: <c>Do</c>) and inline images (Table 90: <c>BI ID EI</c>).</summary>
/// <remarks>
/// ISO 32000-2 §8.8 to §8.10. The reader already delivers an inline image whole (dictionary operand and data range, never
/// tokenized); <c>ID</c> or <c>EI</c> reaching here stand outside any inline image. Forms, images and inline images are run and
/// reported from issue #56, with the image model of issue #60.
/// </remarks>
internal sealed partial class ContentInterpreter
{
    private void ExecuteXObject(ContentOperatorCode code, ContentOperands operands, ReadOperator op, int offset)
    {
        _ = operands;
        _ = op;
        if (code is ContentOperatorCode.BeginInlineImageData or ContentOperatorCode.EndInlineImage)
        {
            Report(ContentIssue.InlineImageInvalid, offset, "ID or EI outside an inline image; ignored.");
        }
    }
}

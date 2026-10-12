namespace Broadside.Parsing;

/// <summary>What turning hint table positions into file offsets needs (F.4.1, F.4.2).</summary>
/// <param name="HeaderOffset">The absolute offset of the <c>%PDF-</c> header; hint positions count from it.</param>
/// <param name="HintOffset">The primary hint stream's offset, <c>H[0]</c>.</param>
/// <param name="HintLength">The primary hint stream's length, <c>H[1]</c>.</param>
/// <param name="FirstPageObjectNumber">The first page's object number, <c>O</c>.</param>
/// <param name="PageCount">The number of pages, <c>N</c>.</param>
internal readonly record struct HintTableLayout(long HeaderOffset, long HintOffset, long HintLength, long FirstPageObjectNumber, long PageCount)
{
    /// <summary>Moves a position past the primary hint stream when it is at or after it (F.4.1), then makes it absolute.</summary>
    /// <param name="position">A position as a hint table states it.</param>
    /// <returns>The absolute file offset.</returns>
    public long Absolute(long position) => HeaderOffset + (position >= HintOffset ? position + HintLength : position);
}

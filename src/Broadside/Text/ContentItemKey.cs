using Broadside.Objects;

namespace Broadside.Text;

/// <summary>
/// A content item glyphs are shown in (ISO 32000-2 §14.7.5): marked-content sequence <see cref="Mcid"/> of the content stream
/// <see cref="Owner"/> (a page dictionary or a form XObject stream), or, with <see cref="Mcid"/> -1, the form XObject
/// <see cref="Owner"/> as an object content item (§14.7.5.3).
/// </summary>
internal readonly record struct ContentItemKey(CosObject Owner, int Mcid);

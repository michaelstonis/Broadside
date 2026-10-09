using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>Which standard attributes are inheritable, and their default values.</summary>
/// <remarks>
/// ISO 32000-2 §14.8.5.3 and Tables 378-385, with the corrected Table 377 of the 2020 errata. A default that depends on the element
/// (Layout <c>Placement</c>: Block or Inline; <c>Color</c> and <c>BorderColor</c>: the text fill colour; Table <c>Scope</c>: computed
/// from position) has no entry: resolution returns <see langword="null"/> for it. <c>TextDecorationType</c> inherits only into
/// directly nested inline-level elements (Table 380); it is treated as not inheritable.
/// </remarks>
internal static class StandardAttributes
{
    private static readonly Dictionary<(string Owner, string Name), (bool Inheritable, CosObject? Default)> Table = Build();

    public static bool IsInheritable(CosName owner, CosName name) => Table.TryGetValue((owner.Value, name.Value), out var entry) && entry.Inheritable;

    public static CosObject? DefaultValue(CosName owner, CosName name) => Table.TryGetValue((owner.Value, name.Value), out var entry) ? entry.Default : null;

    private static Dictionary<(string, string), (bool, CosObject?)> Build()
    {
        CosInteger zero = new(0);
        CosInteger one = new(1);
        var table = new Dictionary<(string, string), (bool, CosObject?)>();
        void Layout(string name, bool inheritable, CosObject? value) => table[("Layout", name)] = (inheritable, value);

        Layout("Placement", false, null);
        Layout("WritingMode", true, new CosName("LrTb"));
        Layout("BackgroundColor", false, null);
        Layout("BorderColor", true, null);
        Layout("BorderStyle", false, new CosName("None"));
        Layout("BorderThickness", true, zero);
        Layout("Padding", false, zero);
        Layout("Color", true, null);
        Layout("SpaceBefore", false, zero);
        Layout("SpaceAfter", false, zero);
        Layout("StartIndent", true, zero);
        Layout("EndIndent", true, zero);
        Layout("TextIndent", true, zero);
        Layout("TextAlign", true, new CosName("Start"));
        Layout("BBox", false, null);
        Layout("Width", false, new CosName("Auto"));
        Layout("Height", false, new CosName("Auto"));
        Layout("BlockAlign", true, new CosName("Before"));
        Layout("InlineAlign", true, new CosName("Start"));
        Layout("TBorderStyle", true, new CosName("None"));
        Layout("TPadding", true, zero);
        Layout("BaselineShift", false, zero);
        Layout("LineHeight", true, new CosName("Normal"));
        Layout("TextPosition", true, new CosName("Normal"));
        Layout("TextDecorationColor", true, null);
        Layout("TextDecorationThickness", true, null);
        Layout("TextDecorationType", false, new CosName("None"));
        Layout("RubyAlign", true, new CosName("Distribute"));
        Layout("RubyPosition", true, new CosName("Before"));
        Layout("GlyphOrientationVertical", true, new CosName("Auto"));
        Layout("ColumnCount", false, one);
        Layout("ColumnGap", false, null);
        Layout("ColumnWidths", false, null);

        table[("List", "ListNumbering")] = (true, new CosName("None"));
        table[("List", "ContinuedList")] = (false, CosBoolean.False);
        table[("List", "ContinuedFrom")] = (false, null);

        table[("PrintField", "Role")] = (false, null);
        table[("PrintField", "Checked")] = (false, new CosName("off"));
        table[("PrintField", "Desc")] = (false, null);

        table[("Table", "RowSpan")] = (false, one);
        table[("Table", "ColSpan")] = (false, one);
        table[("Table", "Headers")] = (false, null);
        table[("Table", "Scope")] = (false, null);
        table[("Table", "Summary")] = (false, null);
        table[("Table", "Short")] = (false, null);
        return table;
    }
}

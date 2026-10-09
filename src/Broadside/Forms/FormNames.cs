using Broadside.Objects;

namespace Broadside.Forms;

/// <summary>Names looked up by the interactive form views (issue #74); kept apart from <see cref="KnownNames"/> so parallel tickets do not collide.</summary>
internal static class FormNames
{
    // Catalog (§7.7.2, Table 29) and interactive form dictionary (§12.7.3, Table 224).
    public static readonly CosName AcroForm = new("AcroForm");
    public static readonly CosName NeedsRendering = new("NeedsRendering");
    public static readonly CosName Fields = new("Fields");
    public static readonly CosName NeedAppearances = new("NeedAppearances");
    public static readonly CosName SigFlags = new("SigFlags");
    public static readonly CosName CO = new("CO");
    public static readonly CosName DR = new("DR");
    public static readonly CosName DA = new("DA");
    public static readonly CosName Q = new("Q");
    public static readonly CosName XFA = new("XFA");

    // Field dictionaries (§12.7.4, Tables 226 and 228).
    public static readonly CosName FT = new("FT");
    public static readonly CosName Parent = new("Parent");
    public static readonly CosName Kids = new("Kids");
    public static readonly CosName T = new("T");
    public static readonly CosName TU = new("TU");
    public static readonly CosName TM = new("TM");
    public static readonly CosName Ff = new("Ff");
    public static readonly CosName V = new("V");
    public static readonly CosName DV = new("DV");
    public static readonly CosName AA = new("AA");
    public static readonly CosName DS = new("DS");
    public static readonly CosName RV = new("RV");

    // Field types (Table 226).
    public static readonly CosName Btn = new("Btn");
    public static readonly CosName Tx = new("Tx");
    public static readonly CosName Ch = new("Ch");
    public static readonly CosName Sig = new("Sig");

    // Field-type entries (Tables 230, 232, 234, 235, 236).
    public static readonly CosName Opt = new("Opt");
    public static readonly CosName MaxLen = new("MaxLen");
    public static readonly CosName TI = new("TI");
    public static readonly CosName I = new("I");
    public static readonly CosName Lock = new("Lock");
    public static readonly CosName SV = new("SV");
    public static readonly CosName Action = new("Action");
    public static readonly CosName P = new("P");
    public static readonly CosName Off = new("Off");
}

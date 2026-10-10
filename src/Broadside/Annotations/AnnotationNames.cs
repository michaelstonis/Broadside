using Broadside.Objects;

namespace Broadside.Annotations;

/// <summary>Names looked up by annotation views (issue #71); kept apart from <see cref="KnownNames"/> so parallel tickets do not collide.</summary>
internal static class AnnotationNames
{
    // Page and common entries (§7.7.3.3 Table 31; §12.5.2 Table 166).
    public static readonly CosName Annots = new("Annots");
    public static readonly CosName Type = new("Type");
    public static readonly CosName Annot = new("Annot");
    public static readonly CosName Subtype = new("Subtype");
    public static readonly CosName Rect = new("Rect");
    public static readonly CosName Contents = new("Contents");
    public static readonly CosName P = new("P");
    public static readonly CosName NM = new("NM");
    public static readonly CosName M = new("M");
    public static readonly CosName F = new("F");
    public static readonly CosName AP = new("AP");
    public static readonly CosName AS = new("AS");
    public static readonly CosName Border = new("Border");
    public static readonly CosName C = new("C");
    public static readonly CosName StructParent = new("StructParent");
    public static readonly CosName OC = new("OC");
    public static readonly CosName AF = new("AF");
    public static readonly CosName NonStrokingOpacity = new("ca");
    public static readonly CosName StrokingOpacity = new("CA");
    public static readonly CosName BM = new("BM");
    public static readonly CosName Lang = new("Lang");

    // Border style and effect (§12.5.4 Tables 168 and 169).
    public static readonly CosName BS = new("BS");
    public static readonly CosName BE = new("BE");
    public static readonly CosName W = new("W");
    public static readonly CosName S = new("S");
    public static readonly CosName D = new("D");
    public static readonly CosName I = new("I");

    // Appearance dictionaries (§12.5.5 Table 170) and form XObjects (§8.10.1 Table 93).
    public static readonly CosName N = new("N");
    public static readonly CosName R = new("R");
    public static readonly CosName Form = new("Form");
    public static readonly CosName BBox = new("BBox");
    public static readonly CosName Matrix = new("Matrix");
    public static readonly CosName Resources = new("Resources");
    public static readonly CosName Group = new("Group");
    public static readonly CosName StructParents = new("StructParents");

    // Markup annotations (§12.5.6.2 Tables 172 and 173).
    public static readonly CosName T = new("T");
    public static readonly CosName Popup = new("Popup");
    public static readonly CosName RC = new("RC");
    public static readonly CosName CreationDate = new("CreationDate");
    public static readonly CosName IRT = new("IRT");
    public static readonly CosName Subj = new("Subj");
    public static readonly CosName RT = new("RT");
    public static readonly CosName IT = new("IT");
    public static readonly CosName ExData = new("ExData");

    // Subtype entries (§12.5.6.4 to §12.5.6.25, Tables 175 to 195, 309, 333, 398, 403).
    public static readonly CosName Open = new("Open");
    public static readonly CosName Name = new("Name");
    public static readonly CosName State = new("State");
    public static readonly CosName StateModel = new("StateModel");
    public static readonly CosName A = new("A");
    public static readonly CosName Dest = new("Dest");
    public static readonly CosName H = new("H");
    public static readonly CosName PA = new("PA");
    public static readonly CosName QuadPoints = new("QuadPoints");
    public static readonly CosName DA = new("DA");
    public static readonly CosName Q = new("Q");
    public static readonly CosName DS = new("DS");
    public static readonly CosName CL = new("CL");
    public static readonly CosName RD = new("RD");
    public static readonly CosName LE = new("LE");
    public static readonly CosName L = new("L");
    public static readonly CosName IC = new("IC");
    public static readonly CosName LL = new("LL");
    public static readonly CosName LLE = new("LLE");
    public static readonly CosName Cap = new("Cap");
    public static readonly CosName LLO = new("LLO");
    public static readonly CosName CP = new("CP");
    public static readonly CosName Measure = new("Measure");
    public static readonly CosName CO = new("CO");
    public static readonly CosName Vertices = new("Vertices");
    public static readonly CosName Path = new("Path");
    public static readonly CosName Sy = new("Sy");
    public static readonly CosName InkList = new("InkList");
    public static readonly CosName Parent = new("Parent");
    public static readonly CosName FS = new("FS");
    public static readonly CosName Sound = new("Sound");
    public static readonly CosName Movie = new("Movie");
    public static readonly CosName MK = new("MK");
    public static readonly CosName AA = new("AA");
    public static readonly CosName MN = new("MN");
    public static readonly CosName LastModified = new("LastModified");
    public static readonly CosName Version = new("Version");
    public static readonly CosName AnnotStates = new("AnnotStates");
    public static readonly CosName FontFauxing = new("FontFauxing");
    public static readonly CosName FixedPrint = new("FixedPrint");
    public static readonly CosName V = new("V");
    public static readonly CosName RO = new("RO");
    public static readonly CosName OverlayText = new("OverlayText");
    public static readonly CosName Repeat = new("Repeat");
    public static readonly CosName ThreeDD = new("3DD");
    public static readonly CosName ThreeDV = new("3DV");
    public static readonly CosName ThreeDA = new("3DA");
    public static readonly CosName ThreeDI = new("3DI");
    public static readonly CosName ThreeDB = new("3DB");
    public static readonly CosName ThreeDU = new("3DU");
    public static readonly CosName GEO = new("GEO");
    public static readonly CosName RichMediaContent = new("RichMediaContent");
    public static readonly CosName RichMediaSettings = new("RichMediaSettings");

    // Appearance characteristics (§12.5.6.19 Table 192).
    public static readonly CosName BC = new("BC");
    public static readonly CosName BG = new("BG");
    public static readonly CosName NormalCaption = new("CA");
    public static readonly CosName AC = new("AC");
    public static readonly CosName RI = new("RI");
    public static readonly CosName IX = new("IX");
    public static readonly CosName IF = new("IF");
    public static readonly CosName TP = new("TP");
}

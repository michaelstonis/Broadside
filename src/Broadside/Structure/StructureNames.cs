using Broadside.Objects;

namespace Broadside.Structure;

/// <summary>Names the logical-structure views look up (ISO 32000-2 §14.6 to §14.8), created once.</summary>
internal static class StructureNames
{
    // Catalog (§7.7.2, Table 29) and mark information (§14.7.1, Table 353).
    public static readonly CosName StructTreeRoot = new("StructTreeRoot");
    public static readonly CosName MarkInfo = new("MarkInfo");
    public static readonly CosName Marked = new("Marked");
    public static readonly CosName UserProperties = new("UserProperties");
    public static readonly CosName Suspects = new("Suspects");
    public static readonly CosName Lang = new("Lang");

    // Structure tree root (Table 354).
    public static readonly CosName K = new("K");
    public static readonly CosName IDTree = new("IDTree");
    public static readonly CosName ParentTree = new("ParentTree");
    public static readonly CosName ParentTreeNextKey = new("ParentTreeNextKey");
    public static readonly CosName RoleMap = new("RoleMap");
    public static readonly CosName ClassMap = new("ClassMap");
    public static readonly CosName Namespaces = new("Namespaces");
    public static readonly CosName PronunciationLexicon = new("PronunciationLexicon");
    public static readonly CosName AF = new("AF");

    // Structure element (Table 355).
    public static readonly CosName StructElem = new("StructElem");
    public static readonly CosName S = new("S");
    public static readonly CosName P = new("P");
    public static readonly CosName ID = new("ID");
    public static readonly CosName Ref = new("Ref");
    public static readonly CosName Pg = new("Pg");
    public static readonly CosName A = new("A");
    public static readonly CosName C = new("C");
    public static readonly CosName R = new("R");
    public static readonly CosName T = new("T");
    public static readonly CosName Alt = new("Alt");
    public static readonly CosName E = new("E");
    public static readonly CosName ActualText = new("ActualText");
    public static readonly CosName NS = new("NS");
    public static readonly CosName PhoneticAlphabet = new("PhoneticAlphabet");
    public static readonly CosName Phoneme = new("Phoneme");

    // Namespaces (Table 356).
    public static readonly CosName Namespace = new("Namespace");
    public static readonly CosName Schema = new("Schema");
    public static readonly CosName RoleMapNS = new("RoleMapNS");

    // Content items (Tables 357-359).
    public static readonly CosName MCR = new("MCR");
    public static readonly CosName OBJR = new("OBJR");
    public static readonly CosName MCID = new("MCID");
    public static readonly CosName Stm = new("Stm");
    public static readonly CosName StmOwn = new("StmOwn");
    public static readonly CosName Obj = new("Obj");
    public static readonly CosName StructParent = new("StructParent");
    public static readonly CosName StructParents = new("StructParents");

    // Attribute objects (Tables 360-362, 376).
    public static readonly CosName O = new("O");
    public static readonly CosName NSO = new("NSO");
    public static readonly CosName Layout = new("Layout");
    public static readonly CosName List = new("List");
    public static readonly CosName PrintField = new("PrintField");
    public static readonly CosName Table = new("Table");
    public static readonly CosName Artifact = new("Artifact");
    public static readonly CosName N = new("N");
    public static readonly CosName V = new("V");
    public static readonly CosName F = new("F");
    public static readonly CosName H = new("H");

    // Artifacts and property lists (Tables 363, 385).
    public static readonly CosName Subtype = new("Subtype");
    public static readonly CosName BBox = new("BBox");
    public static readonly CosName Attached = new("Attached");
}

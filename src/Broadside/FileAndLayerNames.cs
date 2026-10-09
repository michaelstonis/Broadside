using Broadside.Objects;

namespace Broadside;

/// <summary>
/// Names the optional content, file specification, associated file and object metadata views look up (issue #76), created once.
/// Kept apart from <see cref="KnownNames"/> so parallel tickets adding names do not collide.
/// </summary>
internal static class FileAndLayerNames
{
    // File specifications (ISO 32000-2 §7.11, Tables 43-47).
    public static readonly CosName Filespec = new("Filespec");
    public static readonly CosName FS = new("FS");
    public static readonly CosName F = new("F");
    public static readonly CosName UF = new("UF");
    public static readonly CosName DOS = new("DOS");
    public static readonly CosName Mac = new("Mac");
    public static readonly CosName Unix = new("Unix");
    public static readonly CosName ID = new("ID");
    public static readonly CosName V = new("V");
    public static readonly CosName EF = new("EF");
    public static readonly CosName RF = new("RF");
    public static readonly CosName Desc = new("Desc");
    public static readonly CosName CI = new("CI");
    public static readonly CosName Thumb = new("Thumb");
    public static readonly CosName EP = new("EP");
    public static readonly CosName AFRelationship = new("AFRelationship");
    public static readonly CosName URL = new("URL");
    public static readonly CosName Subtype = new("Subtype");
    public static readonly CosName Params = new("Params");
    public static readonly CosName Size = new("Size");
    public static readonly CosName CreationDate = new("CreationDate");
    public static readonly CosName ModDate = new("ModDate");
    public static readonly CosName CheckSum = new("CheckSum");
    public static readonly CosName Unspecified = new("Unspecified");
    public static readonly CosName EmbeddedFile = new("EmbeddedFile");
    public static readonly CosName EmbeddedFiles = new("EmbeddedFiles");

    // Associated files and metadata (§14.13, §14.3.2).
    public static readonly CosName AF = new("AF");
    public static readonly CosName MCAF = new("MCAF");
    public static readonly CosName Metadata = new("Metadata");
    public static readonly CosName XML = new("XML");

    // Optional content (§8.11).
    public static readonly CosName OCProperties = new("OCProperties");
    public static readonly CosName OCGs = new("OCGs");
    public static readonly CosName D = new("D");
    public static readonly CosName Configs = new("Configs");
    public static readonly CosName OCG = new("OCG");
    public static readonly CosName OCMD = new("OCMD");
    public static readonly CosName OC = new("OC");
    public static readonly CosName Name = new("Name");
    public static readonly CosName Intent = new("Intent");
    public static readonly CosName Usage = new("Usage");
    public static readonly CosName P = new("P");
    public static readonly CosName VE = new("VE");
    public static readonly CosName View = new("View");
    public static readonly CosName Design = new("Design");
    public static readonly CosName All = new("All");
    public static readonly CosName AllOn = new("AllOn");
    public static readonly CosName AnyOn = new("AnyOn");
    public static readonly CosName AnyOff = new("AnyOff");
    public static readonly CosName AllOff = new("AllOff");
    public static readonly CosName And = new("And");
    public static readonly CosName Or = new("Or");
    public static readonly CosName Not = new("Not");
    public static readonly CosName Creator = new("Creator");
    public static readonly CosName BaseState = new("BaseState");
    public static readonly CosName ON = new("ON");
    public static readonly CosName OFF = new("OFF");
    public static readonly CosName Unchanged = new("Unchanged");
    public static readonly CosName AS = new("AS");
    public static readonly CosName Order = new("Order");
    public static readonly CosName ListMode = new("ListMode");
    public static readonly CosName AllPages = new("AllPages");
    public static readonly CosName VisiblePages = new("VisiblePages");
    public static readonly CosName RBGroups = new("RBGroups");
    public static readonly CosName Locked = new("Locked");
    public static readonly CosName Event = new("Event");
    public static readonly CosName Category = new("Category");
    public static readonly CosName Print = new("Print");
    public static readonly CosName Export = new("Export");
    public static readonly CosName CreatorInfo = new("CreatorInfo");
    public static readonly CosName Language = new("Language");
    public static readonly CosName Lang = new("Lang");
    public static readonly CosName Preferred = new("Preferred");
    public static readonly CosName ExportState = new("ExportState");
    public static readonly CosName Zoom = new("Zoom");
    public static readonly CosName Min = new("min");
    public static readonly CosName Max = new("max");
    public static readonly CosName PrintState = new("PrintState");
    public static readonly CosName ViewState = new("ViewState");
    public static readonly CosName User = new("User");
    public static readonly CosName PageElement = new("PageElement");
    public static readonly CosName Properties = new("Properties");
    public static readonly CosName XObject = new("XObject");
    public static readonly CosName Annots = new("Annots");

    // Collections (§12.3.5).
    public static readonly CosName Collection = new("Collection");
    public static readonly CosName Schema = new("Schema");
    public static readonly CosName Navigator = new("Navigator");
    public static readonly CosName Colors = new("Colors");
    public static readonly CosName Sort = new("Sort");
    public static readonly CosName Folders = new("Folders");
    public static readonly CosName Split = new("Split");
    public static readonly CosName N = new("N");
    public static readonly CosName O = new("O");
    public static readonly CosName E = new("E");
    public static readonly CosName S = new("S");
    public static readonly CosName A = new("A");
    public static readonly CosName Background = new("Background");
    public static readonly CosName CardBackground = new("CardBackground");
    public static readonly CosName CardBorder = new("CardBorder");
    public static readonly CosName PrimaryText = new("PrimaryText");
    public static readonly CosName SecondaryText = new("SecondaryText");
    public static readonly CosName Direction = new("Direction");
    public static readonly CosName Position = new("Position");
    public static readonly CosName Child = new("Child");
    public static readonly CosName Next = new("Next");
    public static readonly CosName Free = new("Free");
    public static readonly CosName CollectionSubitem = new("CollectionSubitem");
    public static readonly CosName Type = KnownNames.Type;
}

namespace Broadside.Objects;

/// <summary>Names looked up by actions and additional-actions dictionaries (issue #73); kept apart from <see cref="KnownNames"/> and <see cref="NavigationNames"/> so parallel tickets do not collide.</summary>
internal static class ActionNames
{
    /// <summary><c>/AA</c>, an additional-actions dictionary (§12.6.3).</summary>
    public static readonly CosName AA = new("AA");

    /// <summary><c>/OpenAction</c>, the catalog's open action or destination (§7.7.2, Table 29).</summary>
    public static readonly CosName OpenAction = new("OpenAction");

    /// <summary><c>/Base</c>, the base URI of the catalog's URI dictionary (§12.6.4.8, Table 211).</summary>
    public static readonly CosName Base = new("Base");

    /// <summary><c>/JavaScript</c>, the ECMAScript action type (§12.6.4.17) and the name dictionary's tree of document scripts (§7.7.4).</summary>
    public static readonly CosName JavaScript = new("JavaScript");

    /// <summary><c>/JS</c>, a script (Tables 218 and 221).</summary>
    public static readonly CosName JS = new("JS");

    /// <summary><c>/NewWindow</c> (Tables 203, 204 and 207).</summary>
    public static readonly CosName NewWindow = new("NewWindow");

    /// <summary><c>/T</c>: a target dictionary (Tables 204, 205), the hide action's target (Table 214), the movie action's title (Table 213).</summary>
    public static readonly CosName T = new("T");

    /// <summary><c>/R</c>: a target's relationship (Table 205), a rendition action's rendition (Table 218).</summary>
    public static readonly CosName R = new("R");

    /// <summary><c>/N</c>: a target's embedded file name (Table 205), a named action's name (Table 216).</summary>
    public static readonly CosName N = new("N");

    /// <summary><c>/Dp</c>, a go-to-document-part action's DPart (Table 206).</summary>
    public static readonly CosName Dp = new("Dp");

    /// <summary><c>/Win</c>, the launch action's Windows parameters (Table 207).</summary>
    public static readonly CosName Win = new("Win");

    /// <summary><c>/Mac</c>, the launch action's Mac OS parameters (Table 207).</summary>
    public static readonly CosName Mac = new("Mac");

    /// <summary><c>/Unix</c>, the launch action's UNIX parameters (Table 207).</summary>
    public static readonly CosName Unix = new("Unix");

    /// <summary><c>/O</c>: the Windows launch operation (Table 208), the page's open trigger (Table 198).</summary>
    public static readonly CosName O = new("O");

    /// <summary><c>/B</c>, a thread action's bead (Table 209).</summary>
    public static readonly CosName B = new("B");

    /// <summary><c>/Sound</c>, a sound action's sound object (Table 212).</summary>
    public static readonly CosName Sound = new("Sound");

    /// <summary><c>/Volume</c> (Table 212).</summary>
    public static readonly CosName Volume = new("Volume");

    /// <summary><c>/Synchronous</c> (Table 212).</summary>
    public static readonly CosName Synchronous = new("Synchronous");

    /// <summary><c>/Repeat</c> (Table 212).</summary>
    public static readonly CosName Repeat = new("Repeat");

    /// <summary><c>/Mix</c> (Table 212).</summary>
    public static readonly CosName Mix = new("Mix");

    /// <summary><c>/Annotation</c>, a movie action's movie annotation (Table 213).</summary>
    public static readonly CosName Annotation = new("Annotation");

    /// <summary><c>/Operation</c>, a movie action's operation (Table 213).</summary>
    public static readonly CosName Operation = new("Operation");

    /// <summary><c>/H</c>, a hide action's flag (Table 214).</summary>
    public static readonly CosName H = new("H");

    /// <summary><c>/Fields</c> (Tables 239 and 241).</summary>
    public static readonly CosName Fields = new("Fields");

    /// <summary><c>/Flags</c> (Tables 239 and 241).</summary>
    public static readonly CosName Flags = new("Flags");

    /// <summary><c>/CharSet</c> (Table 239).</summary>
    public static readonly CosName CharSet = new("CharSet");

    /// <summary><c>/State</c> (Table 217).</summary>
    public static readonly CosName State = new("State");

    /// <summary><c>/PreserveRB</c> (Table 217).</summary>
    public static readonly CosName PreserveRB = new("PreserveRB");

    /// <summary><c>/AN</c>, a rendition action's screen annotation (Table 218).</summary>
    public static readonly CosName AN = new("AN");

    /// <summary><c>/OP</c>, a rendition action's operation (Table 218).</summary>
    public static readonly CosName OP = new("OP");

    /// <summary><c>/Trans</c>, the transition action type and its transition dictionary (Table 219).</summary>
    public static readonly CosName Trans = new("Trans");

    /// <summary><c>/TA</c>, a target annotation (Tables 220 and 222).</summary>
    public static readonly CosName TA = new("TA");

    /// <summary><c>/V</c>, a 3D view (Table 220).</summary>
    public static readonly CosName V = new("V");

    /// <summary><c>/TI</c>, a rich-media instance (Table 222).</summary>
    public static readonly CosName TI = new("TI");

    /// <summary><c>/CMD</c>, a rich-media command (Table 222).</summary>
    public static readonly CosName CMD = new("CMD");

    /// <summary><c>/ON</c>, <c>/OFF</c>, <c>/Toggle</c>: the states of a set-OCG-state action (Table 217).</summary>
    public static readonly CosName On = new("ON");

    /// <summary><c>/OFF</c> (Table 217).</summary>
    public static readonly CosName Off = new("OFF");

    /// <summary><c>/Toggle</c> (Table 217).</summary>
    public static readonly CosName Toggle = new("Toggle");
}

using Broadside.Objects;

namespace Broadside.Graphics.Functions;

/// <summary>The keys of function dictionaries (ISO 32000-2 §7.10, Tables 38-41) and the <c>Identity</c> name.</summary>
internal static class FunctionNames
{
    /// <summary><c>/FunctionType</c> (Table 38).</summary>
    public static readonly CosName FunctionType = new("FunctionType");

    /// <summary><c>/Domain</c> (Table 38).</summary>
    public static readonly CosName Domain = new("Domain");

    /// <summary><c>/Range</c> (Table 38).</summary>
    public static readonly CosName Range = new("Range");

    /// <summary><c>/Size</c> (Table 39).</summary>
    public static readonly CosName Size = new("Size");

    /// <summary><c>/BitsPerSample</c> (Table 39).</summary>
    public static readonly CosName BitsPerSample = new("BitsPerSample");

    /// <summary><c>/Order</c> (Table 39).</summary>
    public static readonly CosName Order = new("Order");

    /// <summary><c>/Encode</c> (Tables 39 and 41).</summary>
    public static readonly CosName Encode = new("Encode");

    /// <summary><c>/Decode</c> (Table 39).</summary>
    public static readonly CosName Decode = new("Decode");

    /// <summary><c>/C0</c> (Table 40).</summary>
    public static readonly CosName C0 = new("C0");

    /// <summary><c>/C1</c> (Table 40).</summary>
    public static readonly CosName C1 = new("C1");

    /// <summary><c>/N</c> (Table 40).</summary>
    public static readonly CosName N = new("N");

    /// <summary><c>/Functions</c> (Table 41).</summary>
    public static readonly CosName Functions = new("Functions");

    /// <summary><c>/Bounds</c> (Table 41).</summary>
    public static readonly CosName Bounds = new("Bounds");

    /// <summary><c>/Identity</c>, which some entries accept in place of a function (§8.4.5 Table 57, §11.6.5.1 Table 142).</summary>
    public static readonly CosName Identity = new("Identity");
}

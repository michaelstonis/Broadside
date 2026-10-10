namespace Broadside.Objects;

/// <summary>The names of the encryption dictionary and crypt filters (§7.6) that more than one security type reads.</summary>
internal static partial class KnownNames
{
    /// <summary><c>/SubFilter</c>, the format of the security handler's data (§7.6.2, Table 20).</summary>
    public static readonly CosName SubFilter = new("SubFilter");

    /// <summary><c>/Standard</c>, the name of the standard security handler (§7.6.4).</summary>
    public static readonly CosName Standard = new("Standard");

    /// <summary><c>/R</c>, the standard security handler's revision (§7.6.4.2, Table 21).</summary>
    public static readonly CosName R = new("R");

    /// <summary><c>/P</c>, the standard security handler's permissions (§7.6.4.2, Table 21).</summary>
    public static readonly CosName P = new("P");

    /// <summary><c>/CF</c>, the crypt filters of an encryption dictionary (§7.6.2, Table 20).</summary>
    public static readonly CosName CF = new("CF");

    /// <summary><c>/StmF</c>, the crypt filter streams are decrypted with by default (§7.6.2, Table 20).</summary>
    public static readonly CosName StmF = new("StmF");

    /// <summary><c>/StrF</c>, the crypt filter strings are decrypted with (§7.6.2, Table 20).</summary>
    public static readonly CosName StrF = new("StrF");

    /// <summary><c>/EFF</c>, the crypt filter embedded files are decrypted with (§7.6.2, Table 20).</summary>
    public static readonly CosName EFF = new("EFF");

    /// <summary><c>/CFM</c>, a crypt filter's method (§7.6.6, Table 25).</summary>
    public static readonly CosName CFM = new("CFM");

    /// <summary><c>/AESV2</c>, the AES-128 crypt filter method (§7.6.6, Table 25).</summary>
    public static readonly CosName AESV2 = new("AESV2");

    /// <summary><c>/AESV3</c>, the AES-256 crypt filter method (§7.6.6, Table 25).</summary>
    public static readonly CosName AESV3 = new("AESV3");

    /// <summary><c>/EncryptMetadata</c>, whether metadata streams are encrypted (§7.6.4.2 Table 21, §7.6.5.2 Table 27).</summary>
    public static readonly CosName EncryptMetadata = new("EncryptMetadata");

    /// <summary><c>/ByteRange</c>, the signed byte ranges of a signature dictionary (§12.8.1, Table 255).</summary>
    public static readonly CosName ByteRange = new("ByteRange");
}

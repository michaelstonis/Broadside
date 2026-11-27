namespace Broadside;

/// <summary>
/// Options for <see cref="PdfDocument.AttachInvoice"/>: the invoice's profile, the embedded file's name and relationship, and the
/// <c>fx:Version</c> the XMP packet declares.
/// </summary>
/// <remarks>
/// <para>
/// Factur-X 1.0 and ZUGFeRD 2.x hybrid invoices: an ISO 19005-3 (PDF/A-3) file with the CII XML as an associated file
/// (ISO 32000-2 §14.13.3) and the <c>fx</c> XMP properties. The defaults follow the profile:
/// </para>
/// <list type="table">
/// <listheader><term>Profile</term><description><c>fx:ConformanceLevel</c>, file name, <c>AFRelationship</c></description></listheader>
/// <item><term><see cref="PdfInvoiceProfile.Minimum"/></term><description><c>MINIMUM</c>, <c>factur-x.xml</c>, <c>Data</c></description></item>
/// <item><term><see cref="PdfInvoiceProfile.BasicWl"/></term><description><c>BASIC WL</c>, <c>factur-x.xml</c>, <c>Data</c></description></item>
/// <item><term><see cref="PdfInvoiceProfile.Basic"/></term><description><c>BASIC</c>, <c>factur-x.xml</c>, <c>Alternative</c></description></item>
/// <item><term><see cref="PdfInvoiceProfile.EN16931"/></term><description><c>EN 16931</c>, <c>factur-x.xml</c>, <c>Alternative</c></description></item>
/// <item><term><see cref="PdfInvoiceProfile.Extended"/></term><description><c>EXTENDED</c>, <c>factur-x.xml</c>, <c>Alternative</c></description></item>
/// <item><term><see cref="PdfInvoiceProfile.XRechnung"/></term><description><c>XRECHNUNG</c>, <c>xrechnung.xml</c>, <c>Alternative</c></description></item>
/// </list>
/// <para>Per call: a mutable instance whose <c>With*</c> members return it, so calls chain.</para>
/// </remarks>
public sealed class PdfInvoiceOptions
{
    private PdfInvoiceProfile _profile = PdfInvoiceProfile.EN16931;
    private string? _fileName;
    private string _version = "1.0";
    private PdfFileRelationship? _relationship;

    /// <summary>Gets or sets the invoice's profile, written as <c>fx:ConformanceLevel</c>; <see cref="PdfInvoiceProfile.EN16931"/> by default.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined profile.</exception>
    public PdfInvoiceProfile Profile
    {
        get => _profile;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not an invoice profile.");
            }

            _profile = value;
        }
    }

    /// <summary>
    /// Gets or sets the embedded file's name, written to the file specification, the <c>EmbeddedFiles</c> key and
    /// <c>fx:DocumentFileName</c>; <see langword="null"/>, the default, takes the profile's (<c>factur-x.xml</c>, or
    /// <c>xrechnung.xml</c> for <see cref="PdfInvoiceProfile.XRechnung"/>).
    /// </summary>
    /// <exception cref="ArgumentException">The value is empty.</exception>
    public string? FileName
    {
        get => _fileName;
        set
        {
            if (value is { Length: 0 })
            {
                throw new ArgumentException("A file name is not empty; pass null for the profile's.", nameof(value));
            }

            _fileName = value;
        }
    }

    /// <summary>
    /// Gets or sets <c>fx:Version</c>, the version of the specification the XML follows; <c>1.0</c> by default, the version of
    /// Factur-X 1.0 and ZUGFeRD 2.x.
    /// </summary>
    /// <exception cref="ArgumentException">The value is null or empty.</exception>
    public string Version
    {
        get => _version;
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            _version = value;
        }
    }

    /// <summary>
    /// Gets or sets the file's <c>AFRelationship</c>; <see langword="null"/>, the default, takes the profile's (<c>Data</c> for
    /// <see cref="PdfInvoiceProfile.Minimum"/> and <see cref="PdfInvoiceProfile.BasicWl"/>, <c>Alternative</c> for the others).
    /// </summary>
    /// <remarks>ISO 32000-2 §7.11.3, Table 43, and §14.13.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is <see cref="PdfFileRelationship.Other"/> or not a defined value.</exception>
    public PdfFileRelationship? Relationship
    {
        get => _relationship;
        set
        {
            if (value is { } relationship && (relationship == PdfFileRelationship.Other || !Enum.IsDefined(relationship)))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Not a relationship Table 43 names.");
            }

            _relationship = value;
        }
    }

    /// <summary>
    /// Gets or sets the embedded file's modification date (<c>Params</c> <c>ModDate</c>, which ISO 19005-3 §6.8 expects of an
    /// associated file); <see langword="null"/>, the default, takes the document clock's time of the call.
    /// </summary>
    public DateTimeOffset? ModificationDate { get; set; }

    /// <summary>Sets <see cref="Profile"/>.</summary>
    /// <param name="profile">The profile.</param>
    /// <returns>These options.</returns>
    public PdfInvoiceOptions WithProfile(PdfInvoiceProfile profile)
    {
        Profile = profile;
        return this;
    }

    /// <summary>Sets <see cref="FileName"/>.</summary>
    /// <param name="name">The file name, or <see langword="null"/> for the profile's.</param>
    /// <returns>These options.</returns>
    public PdfInvoiceOptions WithFileName(string? name)
    {
        FileName = name;
        return this;
    }

    /// <summary>Sets <see cref="Version"/>.</summary>
    /// <param name="version">The version, such as <c>1.0</c>.</param>
    /// <returns>These options.</returns>
    public PdfInvoiceOptions WithVersion(string version)
    {
        Version = version;
        return this;
    }

    /// <summary>Sets <see cref="Relationship"/>.</summary>
    /// <param name="relationship">The relationship, or <see langword="null"/> for the profile's.</param>
    /// <returns>These options.</returns>
    public PdfInvoiceOptions WithRelationship(PdfFileRelationship? relationship)
    {
        Relationship = relationship;
        return this;
    }

    /// <summary>Sets <see cref="ModificationDate"/>.</summary>
    /// <param name="date">The date, or <see langword="null"/> for the time of the call.</param>
    /// <returns>These options.</returns>
    public PdfInvoiceOptions WithModificationDate(DateTimeOffset? date)
    {
        ModificationDate = date;
        return this;
    }
}

namespace Broadside.BuilderSamples;

/// <summary>A field report: a quarter's notes and incidents for each region.</summary>
/// <param name="Quarter">The quarter, such as <c>Q4 2026</c>.</param>
/// <param name="Team">The team that wrote it.</param>
/// <param name="Summary">The opening summary.</param>
/// <param name="Regions">The regions, in order.</param>
public sealed record FieldReport(string Quarter, string Team, string Summary, IReadOnlyList<Region> Regions);

/// <summary>A region of a field report.</summary>
/// <param name="Name">The region's name.</param>
/// <param name="Notes">The notes, one paragraph each.</param>
/// <param name="Incidents">The incidents reported.</param>
public sealed record Region(string Name, IReadOnlyList<string> Notes, IReadOnlyList<string> Incidents);

/// <summary>Units shipped in a region in two quarters.</summary>
/// <param name="Name">The region's name.</param>
/// <param name="Q3">Units shipped in the third quarter.</param>
/// <param name="Q4">Units shipped in the fourth quarter.</param>
public sealed record RegionResult(string Name, int Q3, int Q4)
{
    /// <summary>Gets the change from the third quarter to the fourth, as a fraction.</summary>
    public double Change => (Q4 - Q3) / (double)Q3;
}

/// <summary>A postal address.</summary>
/// <param name="Name">The addressee.</param>
/// <param name="Lines">The street, town and postcode lines.</param>
/// <param name="Country">The country.</param>
/// <param name="Iban">The bank account, for a seller.</param>
public sealed record Address(string Name, IReadOnlyList<string> Lines, string Country, string Iban = "");

/// <summary>A line of an invoice.</summary>
/// <param name="Description">What was sold.</param>
/// <param name="Quantity">How many.</param>
/// <param name="UnitPrice">The price of one.</param>
/// <param name="Note">A note shown under the line, or null.</param>
public sealed record InvoiceLine(string Description, int Quantity, decimal UnitPrice, string? Note = null)
{
    /// <summary>Gets the line's amount.</summary>
    public decimal Amount => Quantity * UnitPrice;
}

/// <summary>An invoice.</summary>
/// <param name="Number">The invoice number.</param>
/// <param name="IssuedOn">The date of issue.</param>
/// <param name="DueOn">The date payment is due.</param>
/// <param name="Seller">Who sells.</param>
/// <param name="Buyer">Who buys.</param>
/// <param name="Lines">What was sold.</param>
/// <param name="Discount">The discount off the subtotal.</param>
/// <param name="VatRate">The VAT rate, as a fraction.</param>
public sealed record Invoice(string Number, DateOnly IssuedOn, DateOnly DueOn, Address Seller, Address Buyer, IReadOnlyList<InvoiceLine> Lines, decimal Discount, decimal VatRate)
{
    /// <summary>Gets the sum of the lines.</summary>
    public decimal Subtotal => Lines.Sum(line => line.Amount);

    /// <summary>Gets the VAT on the subtotal less the discount.</summary>
    public decimal Vat => decimal.Round((Subtotal - Discount) * VatRate, 2);

    /// <summary>Gets the amount due.</summary>
    public decimal Total => Subtotal - Discount + Vat;

    /// <summary>Gets a value indicating whether the invoice is past its due date on 14 December 2026, the samples' fixed today.</summary>
    public bool IsOverdue => DueOn < new DateOnly(2026, 12, 14);

    /// <summary>The invoice as a minimal XML document, attached to the PDF as its machine-readable alternative.</summary>
    /// <returns>The XML.</returns>
    public string ToXml() => string.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Invoice><ID>{Number}</ID><IssueDate>{IssuedOn:yyyy-MM-dd}</IssueDate><DueDate>{DueOn:yyyy-MM-dd}</DueDate><Total>{Total:0.00}</Total></Invoice>\n");
}

/// <summary>The invented data the samples are built from.</summary>
public static class SampleData
{
    /// <summary>Gets the field report.</summary>
    public static FieldReport Report { get; } = new(
        "Q4 2026",
        "Field Operations",
        "Flow was steady across every region this quarter. Two short outages in the east were traced to a failed pressure sensor and fixed within the day; every other site met its reading schedule.",
        [
            new Region("North", [.. Notes("North", 9)], []),
            new Region("South", [.. Notes("South", 8)], ["A meter at Riverside read 4% high and was recalibrated."]),
            new Region("East", [.. Notes("East", 10)], ["Pressure sensor failure at Hilltop, 3 hours.", "Telemetry gap at Marsh Lane, 40 minutes."]),
            new Region("West", [.. Notes("West", 7)], []),
        ]);

    /// <summary>Gets the regional results.</summary>
    public static IReadOnlyList<RegionResult> Results { get; } =
    [
        new("North", 120, 135),
        new("South", 98, 104),
        new("East", 143, 151),
        new("West", 115, 112),
    ];

    /// <summary>Gets the invoice.</summary>
    public static Invoice Invoice { get; } = new(
        "2026-0042",
        new DateOnly(2026, 11, 14),
        new DateOnly(2026, 12, 14),
        new Address("Acme Instruments Ltd", ["Unit 4, Canal Works", "Leeds LS11 5QR"], "United Kingdom", "GB33 BUKB 2020 1555 5555 55"),
        new Address("Northern Water Ltd", ["1 Reservoir Road", "York YO1 7HH"], "United Kingdom"),
        [
            new InvoiceLine("Flow meter, 50 mm", 2, 420m),
            new InvoiceLine("Calibration service", 1, 180m, "Calibrated on site at the North pumping station."),
            new InvoiceLine("Mounting kit", 4, 35.50m),
        ],
        50m,
        0.2m);

    private static IEnumerable<string> Notes(string region, int count)
    {
        for (int note = 1; note <= count; note++)
        {
            yield return $"{region} site {note}: the meters were read on schedule, the readings agree with the telemetry within tolerance, and the next visit is booked.";
        }
    }
}

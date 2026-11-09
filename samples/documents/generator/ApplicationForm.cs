using System.Text;
using Broadside.Annotations;
using Broadside.Fonts;
using Broadside.Forms;
using Broadside.Graphics;
using Broadside.Objects;
using static Broadside.SampleDocuments.Documents;

namespace Broadside.SampleDocuments;

/// <summary>
/// A community garden's plot application, the kind of interactive form an office tool exports: every AcroForm field type (text,
/// comb, multi-line, read-only, check boxes, radio buttons, an editable combo box, a multi-select list box, push buttons and an
/// unsigned signature field), Acrobat-style JavaScript for keystroke, format and calculation actions with a calculation order, a
/// document-level script, and reset, print and submit buttons. Broadside does not run the scripts; the stored values and their
/// appearances are what a viewer shows. A second file adds an XFA form (a hybrid form) in an incremental update.
/// </summary>
internal static class ApplicationForm
{
    private const double PageWidth = 612;
    private const double PageHeight = 792;
    private const double Left = 54;
    private const double Right = PageWidth - 54;

    private static readonly string[] States = ["CA", "CO", "IL", "MA", "MN", "NY", "OR", "TX", "WA"];

    private static readonly string[] Crops =
        ["Beans", "Beets", "Carrots", "Garlic", "Herbs", "Kale", "Lettuce", "Onions", "Peas", "Potatoes", "Pumpkins", "Squash", "Strawberries", "Sunflowers", "Tomatoes"];

    public static void Write(GeneratorContext context)
    {
        string path = context.OutputPath("application-form.pdf");
        WriteForm(context, path);
        WriteXfa(path, context.OutputPath("application-form-xfa.pdf"));
    }

    private static void WriteForm(GeneratorContext context, string path)
    {
        using PdfDocument document = PdfDocument.Create();
        document.Pages.RemoveAt(0);
        SetProperties(document, "Plot application 2027", "A fillable application form with calculated fields, written for the Broadside sample documents.");
        PdfCanvasFont regular = context.Font(document, "firasans/FiraSans-Regular.ttf");
        PdfCanvasFont bold = context.Font(document, "firasans/FiraSans-Bold.ttf");
        PdfCanvasFont light = context.Font(document, "firasans/FiraSans-Light.ttf");
        PdfColor green = Rgb(0.18, 0.42, 0.22);
        PdfColor ink = Rgb(0.13, 0.15, 0.13);
        PdfColor muted = Rgb(0.42, 0.45, 0.42);

        PdfPage one = document.Pages.Add(new PdfRectangle(0, 0, PageWidth, PageHeight));
        PdfPage two = document.Pages.Add(new PdfRectangle(0, 0, PageWidth, PageHeight));
        using (PdfCanvas canvas = one.CreateCanvas())
        {
            Banner(canvas, bold, light, green, "Plot Application 2027", "Riverside Community Garden · an invented garden for a sample document");
            canvas.SetFillColor(ink);
            Section(canvas, bold, green, 650, "1  About you");
            Label(canvas, regular, ink, Left, 626, "Full name");
            Label(canvas, regular, ink, Left, 598, "Street address");
            Label(canvas, regular, ink, Left, 570, "City");
            Label(canvas, regular, ink, 330, 570, "State");
            Label(canvas, regular, ink, 440, 570, "ZIP code");
            Label(canvas, regular, ink, Left, 542, "Phone");
            Label(canvas, regular, ink, 300, 542, "Email");
            Label(canvas, regular, ink, Left, 514, "People in your household");
            Label(canvas, regular, ink, 300, 514, "How did you hear about us?");

            Section(canvas, bold, green, 474, "2  Your plot");
            Label(canvas, regular, ink, Left, 450, "Choose one plot type:");
            Label(canvas, regular, ink, Left + 22, 428, "Half plot, 10 × 10 ft · $35");
            Label(canvas, regular, ink, Left + 22, 408, "Full plot, 10 × 20 ft · $60");
            Label(canvas, regular, ink, Left + 22, 388, "Raised bed, wheelchair accessible · $25");
            Label(canvas, regular, ink, 330, 450, "Extras:");
            Label(canvas, regular, ink, 352, 428, "Tool locker · $15");
            Label(canvas, regular, ink, 352, 408, "Compost bin · $10");
            Label(canvas, regular, ink, 352, 388, "Water key deposit · $20");
            Label(canvas, regular, ink, Left, 356, "What will you grow? (Choose several.)");
            Label(canvas, regular, ink, 330, 356, "Gardening experience");

            Section(canvas, bold, green, 236, "3  Fees");
            string[] fees = ["Plot fee", "Extras", "Volunteer discount (%)", "Total due"];
            for (int i = 0; i < fees.Length; i++)
            {
                Label(canvas, i == 3 ? bold : regular, ink, Left, 212 - (i * 26), fees[i]);
            }

            canvas.SetFillColor(muted);
            Line(canvas, light, 9, Left + 270, 212, "Members who volunteer 20 hours a season get 25% off.");
            Line(canvas, light, 9, Left + 270, 200, "The totals update as you type in viewers that run form scripts.");
            Footer(canvas, light, muted, 1);
        }

        using (PdfCanvas canvas = two.CreateCanvas())
        {
            Banner(canvas, bold, light, green, "Plot Application 2027", "Riverside Community Garden · continued");
            Section(canvas, bold, green, 650, "4  Garden rules");
            canvas.SetFillColor(ink);
            string[] rules =
            [
                "1. Plots must be planted by June 1 and kept free of weeds through the season.",
                "2. No chemical pesticides or herbicides. Organic methods only.",
                "3. Water is available from April 15 to October 15. Turn taps off after use.",
                "4. Paths between plots are shared: keep them clear and mow your half.",
                "5. Each plotholder gives four hours a season to common areas.",
                "6. Plots left untended for four weeks may be reassigned after one written warning.",
                "7. Children are welcome when supervised. Dogs on a leash only.",
            ];
            double y = 626;
            foreach (string rule in rules)
            {
                Line(canvas, regular, 10, Left, y, rule);
                y -= 18;
            }

            Label(canvas, regular, ink, Left + 22, 482, "I have read the garden rules and agree to follow them.");
            Section(canvas, bold, green, 444, "5  Signature");
            Label(canvas, regular, ink, Left, 420, "Signature");
            Label(canvas, regular, ink, 380, 420, "Date (mm/dd/yyyy)");
            Label(canvas, regular, ink, Left, 300, "Anything else we should know?");
            canvas.SetFillColor(muted);
            Line(canvas, light, 9, Left, 120, "Office use only: the application number is assigned when the form is received.");
            Footer(canvas, light, muted, 2);
        }

        PdfAcroForm form = document.CreateAcroForm();
        var border = new PdfDeviceColor(0.55, 0.62, 0.55);
        var fill = new PdfDeviceColor(0.95, 0.97, 0.95);
        PdfFieldOptions Options(string tooltip) => new PdfFieldOptions().WithFontSize(10).WithBorderColor(border).WithBackgroundColor(fill).WithBorderWidth(0.75).WithAlternateName(tooltip);
        static PdfRectangle Box(double x, double y, double width, double height = 18) => new(x, y - 5, x + width, y - 5 + height);

        form.AddTextField("applicant.name", one, Box(Left + 110, 626, Right - Left - 110), Options("Your full name").WithFlags(PdfFieldFlags.Required));
        form.AddTextField("applicant.street", one, Box(Left + 110, 598, Right - Left - 110), Options("Street address"));
        form.AddTextField("applicant.city", one, Box(Left + 110, 570, 160), Options("City"));
        PdfComboBoxField state = form.AddComboBox("applicant.state", one, Box(366, 570, 64),
            States.Select(static s => new PdfChoiceOption(s)),
            Options("State (choose or type)").WithFlags(PdfFieldFlags.Edit));
        state.SetValue("OR");
        PdfTextField zip = form.AddTextField("applicant.zip", one, Box(494, 570, Right - 494), Options("Five-digit ZIP code").WithMaxLength(5).WithFlags(PdfFieldFlags.Comb));
        PdfTextField phone = form.AddTextField("applicant.phone", one, Box(Left + 110, 542, 120), Options("Phone number"));
        form.AddTextField("applicant.email", one, Box(340, 542, Right - 340), Options("Email address"));
        PdfTextField household = form.AddTextField("applicant.household", one, Box(Left + 150, 514, 40), Options("Number of people in your household").WithQuadding(PdfTextJustification.Centered));
        form.AddComboBox("applicant.referral", one, Box(440, 514, Right - 440),
            [new PdfChoiceOption("friend", "A friend or neighbor"), new PdfChoiceOption("library", "Library notice board"), new PdfChoiceOption("web", "Our website"), new PdfChoiceOption("fair", "Spring plant fair")],
            Options("How you heard about the garden"));

        PdfRadioButtonField plot = form.AddRadioButtons("plot.type",
            [new PdfRadioButton(one, new PdfRectangle(Left + 2, 424, Left + 14, 436), "half"),
             new PdfRadioButton(one, new PdfRectangle(Left + 2, 404, Left + 14, 416), "full"),
             new PdfRadioButton(one, new PdfRectangle(Left + 2, 384, Left + 14, 396), "raised")],
            Options("Plot type").WithFlags(PdfFieldFlags.NoToggleToOff));
        PdfCheckBoxField locker = form.AddCheckBox("extras.locker", one, new PdfRectangle(332, 424, 344, 436), Options("Tool locker, $15"));
        PdfCheckBoxField compost = form.AddCheckBox("extras.compost", one, new PdfRectangle(332, 404, 344, 416), Options("Compost bin, $10"));
        PdfCheckBoxField water = form.AddCheckBox("extras.water", one, new PdfRectangle(332, 384, 344, 396), Options("Water key deposit, $20"));
        PdfListBoxField crops = form.AddListBox("plot.crops", one, new PdfRectangle(Left, 262, Left + 240, 345),
            Crops
                .Select(static c => new PdfChoiceOption(c)),
            Options("What you plan to grow").WithFlags(PdfFieldFlags.MultiSelect));
        crops.SetSelectedIndices([4, 6, 14]);
        form.AddListBox("plot.experience", one, new PdfRectangle(330, 290, Right, 345),
            [new PdfChoiceOption("none", "First season"), new PdfChoiceOption("some", "A few seasons"), new PdfChoiceOption("lots", "Many seasons")],
            Options("Your gardening experience"));

        PdfTextField plotFee = form.AddTextField("fees.plot", one, Box(Left + 150, 212, 90), Options("Plot fee (calculated)").WithFlags(PdfFieldFlags.ReadOnly).WithQuadding(PdfTextJustification.Right));
        PdfTextField extrasFee = form.AddTextField("fees.extras", one, Box(Left + 150, 186, 90), Options("Extras (calculated)").WithFlags(PdfFieldFlags.ReadOnly).WithQuadding(PdfTextJustification.Right));
        PdfTextField discount = form.AddTextField("fees.discount", one, Box(Left + 150, 160, 90), Options("Volunteer discount in percent").WithQuadding(PdfTextJustification.Right));
        PdfTextField total = form.AddTextField("fees.total", one, Box(Left + 150, 134, 90), Options("Total due (calculated)").WithFlags(PdfFieldFlags.ReadOnly).WithQuadding(PdfTextJustification.Right));

        PdfCheckBoxField agree = form.AddCheckBox("rules.agree", two, new PdfRectangle(Left + 2, 478, Left + 14, 490), Options("I agree to the garden rules").WithFlags(PdfFieldFlags.Required));
        form.AddSignatureField("applicant.signature", two, new PdfRectangle(Left, 340, Left + 300, 410), new PdfFieldOptions().WithAlternateName("Sign here"));
        PdfTextField date = form.AddTextField("applicant.date", two, Box(380, 392, Right - 380), Options("Date you sign, mm/dd/yyyy"));
        form.AddTextField("applicant.notes", two, new PdfRectangle(Left, 160, Right, 290), Options("Anything else we should know").WithFlags(PdfFieldFlags.Multiline));
        PdfPushButtonField reset = form.AddPushButton("buttons.reset", two, new PdfRectangle(Left, 70, Left + 110, 94), Options("Clear every field").WithCaption("Clear form"));
        PdfPushButtonField print = form.AddPushButton("buttons.print", two, new PdfRectangle(Left + 125, 70, Left + 235, 94), Options("Print the form").WithCaption("Print"));
        PdfPushButtonField submit = form.AddPushButton("buttons.submit", two, new PdfRectangle(Right - 140, 70, Right, 94), Options("Send the application to the garden office").WithCaption("Submit application"));

        // Values a person might have typed: the calculated fields hold what the scripts would have computed.
        plot.SetValue(new CosName("full"));
        compost.SetChecked(true);
        household.SetValue("3");
        plotFee.SetValue("60.00");
        extrasFee.SetValue("10.00");
        discount.SetValue("0");
        total.SetValue("70.00");
        _ = locker;
        _ = water;
        _ = agree;

        // Acrobat's form scripts (keystroke, format, calculate) and the buttons' actions, written as COS (Broadside keeps them).
        Actions(document, zip, keystroke: "AFSpecial_Keystroke(0);", format: "AFSpecial_Format(0);");
        Actions(document, phone, keystroke: "AFSpecial_Keystroke(2);", format: "AFSpecial_Format(2);");
        Actions(document, household, keystroke: "AFNumber_Keystroke(0, 0, 0, 0, \"\", true);", format: "AFNumber_Format(0, 0, 0, 0, \"\", true);");
        Actions(document, discount, keystroke: "AFPercent_Keystroke(0, 0);", format: null);
        Actions(document, date, keystroke: "AFDate_KeystrokeEx(\"mm/dd/yyyy\");", format: "AFDate_FormatEx(\"mm/dd/yyyy\");");
        Actions(document, plotFee, keystroke: null, format: "AFNumber_Format(2, 0, 0, 0, \"$\", true);",
            calculate: "var t = this.getField(\"plot.type\").value; event.value = t == \"half\" ? 35 : t == \"full\" ? 60 : t == \"raised\" ? 25 : 0;");
        Actions(document, extrasFee, keystroke: null, format: "AFNumber_Format(2, 0, 0, 0, \"$\", true);",
            calculate: "var s = 0; if (this.getField(\"extras.locker\").value != \"Off\") s += 15; if (this.getField(\"extras.compost\").value != \"Off\") s += 10; if (this.getField(\"extras.water\").value != \"Off\") s += 20; event.value = s;");
        Actions(document, total, keystroke: null, format: "AFNumber_Format(2, 0, 0, 0, \"$\", true);",
            calculate: "AFSimple_Calculate(\"SUM\", new Array(\"fees.plot\", \"fees.extras\")); event.value = event.value * (1 - this.getField(\"fees.discount\").value / 100);");
        form.Dictionary[new CosName("CO")] = new CosArray([plotFee.Reference!, extrasFee.Reference!, total.Reference!]);

        reset.Dictionary[new CosName("A")] = new CosDictionary
        {
            [new CosName("S")] = new CosName("ResetForm"),
        };
        print.Dictionary[new CosName("A")] = JavaScript("this.print({bUI: true, bSilent: false, bShrinkToFit: true});");
        var submitAction = new CosDictionary
        {
            [new CosName("S")] = new CosName("SubmitForm"),
            [new CosName("F")] = new CosDictionary
            {
                [new CosName("FS")] = new CosName("URL"),
                [new CosName("F")] = Text("https://garden.example.org/apply"),
            },
            [new CosName("Flags")] = new CosInteger(4), // ExportFormat: HTML form format
        };
        submit.Dictionary[new CosName("A")] = submitAction;

        // A document-level script, as Acrobat stores one: the Names dictionary's JavaScript name tree.
        PdfNameTree scripts = document.CreateNameTree([new KeyValuePair<CosString, CosObject>(Text("Welcome"),
            document.AddObject(JavaScript("if (this.getField(\"applicant.name\").value == \"\") app.alert(\"Welcome! Start with your name on page 1.\", 3);")))]);
        document.Catalog[new CosName("Names")] = new CosDictionary { [new CosName("JavaScript")] = (CosObject?)scripts.RootReference ?? scripts.Root };

        document.Save(path);
    }

    /// <summary>Adds an XFA form (template, datasets, configuration) to the AcroForm, as a hybrid form keeps both, in an update.</summary>
    private static void WriteXfa(string source, string target)
    {
        using PdfDocument document = PdfDocument.Open(File.ReadAllBytes(source));
        string[] names = ["applicant.name", "applicant.city", "applicant.zip", "plot.type", "fees.total"];
        var template = new StringBuilder();
        template.Append("<template xmlns=\"http://www.xfa.org/schema/xfa-template/3.3/\"><subform name=\"form1\" layout=\"tb\" locale=\"en_US\"><pageSet><pageArea name=\"Page1\"><contentArea x=\"0.75in\" y=\"0.75in\" w=\"7in\" h=\"9.5in\"/><medium stock=\"letter\" short=\"8.5in\" long=\"11in\"/></pageArea></pageSet><subform w=\"7in\" h=\"9.5in\">");
        foreach (string name in names)
        {
            template.Append("<field name=\"").Append(name.Replace('.', '_')).Append("\" w=\"3in\" h=\"0.3in\"><ui><textEdit/></ui><bind match=\"global\"/></field>");
        }

        template.Append("</subform></subform></template>");
        string datasets = "<xfa:datasets xmlns:xfa=\"http://www.xfa.org/schema/xfa-data/1.0/\"><xfa:data><form1><applicant_city>Portland</applicant_city><plot_type>full</plot_type><fees_total>70.00</fees_total></form1></xfa:data></xfa:datasets>";
        string config = "<config xmlns=\"http://www.xfa.org/schema/xci/3.1/\"><present><pdf><version>1.7</version><interactive>1</interactive></pdf></present><acrobat><acrobat7><dynamicRender>forbidden</dynamicRender></acrobat7></acrobat></config>";
        var packets = new CosArray();
        foreach ((string name, string xml) in new[]
        {
            ("preamble", "<xdp:xdp xmlns:xdp=\"http://ns.adobe.com/xdp/\" timeStamp=\"2027-02-01T09:00:00Z\" uuid=\"3f1c1f1e-5b5d-4c47-9d4c-6a7f2b8e0a51\">"),
            ("config", config),
            ("template", template.ToString()),
            ("datasets", datasets),
            ("postamble", "</xdp:xdp>"),
        })
        {
            packets.Add(Text(name));
            packets.Add(document.AddObject(new CosStream(new CosDictionary(), Encoding.UTF8.GetBytes(xml))));
        }

        document.AcroForm!.Dictionary[new CosName("XFA")] = packets;
        using FileStream output = File.Create(target);
        document.Save(output, new PdfSaveOptions().WithIncrementalUpdate(true).WithUpdateModificationDate(false));
    }

    private static void Actions(PdfDocument document, PdfField field, string? keystroke, string? format, string? calculate = null)
    {
        var actions = new CosDictionary();
        if (keystroke is not null)
        {
            actions[new CosName("K")] = document.AddObject(JavaScript(keystroke));
        }

        if (format is not null)
        {
            actions[new CosName("F")] = document.AddObject(JavaScript(format));
        }

        if (calculate is not null)
        {
            actions[new CosName("C")] = document.AddObject(JavaScript(calculate));
        }

        field.Dictionary[new CosName("AA")] = actions;
    }

    private static CosDictionary JavaScript(string script) => new()
    {
        [new CosName("S")] = new CosName("JavaScript"),
        [new CosName("JS")] = Text(script),
    };

    private static CosString Text(string text) => new(Encoding.ASCII.GetBytes(text));

    private static void Banner(PdfCanvas canvas, PdfCanvasFont bold, PdfCanvasFont light, PdfColor green, string title, string subtitle)
    {
        canvas.SetFillColor(green);
        canvas.Rectangle(0, PageHeight - 92, PageWidth, 92);
        canvas.Fill();

        // A leaf: two arcs meeting at the tips, and its midrib.
        canvas.SetFillColor(Rgb(0.75, 0.88, 0.62));
        canvas.MoveTo(Left, PageHeight - 70);
        canvas.CurveTo(Left + 4, PageHeight - 40, Left + 26, PageHeight - 30, Left + 40, PageHeight - 28);
        canvas.CurveTo(Left + 36, PageHeight - 52, Left + 22, PageHeight - 70, Left, PageHeight - 70);
        canvas.Fill();
        canvas.SetStrokeColor(green);
        canvas.SetLineWidth(1.2);
        canvas.MoveTo(Left + 2, PageHeight - 68);
        canvas.CurveTo(Left + 14, PageHeight - 54, Left + 26, PageHeight - 40, Left + 38, PageHeight - 30);
        canvas.Stroke();

        canvas.SetFillColor(Gray(1));
        Line(canvas, bold, 24, Left + 54, PageHeight - 52, title);
        Line(canvas, light, 10.5, Left + 54, PageHeight - 70, subtitle);
    }

    private static void Section(PdfCanvas canvas, PdfCanvasFont bold, PdfColor green, double y, string text)
    {
        canvas.SetFillColor(green);
        Line(canvas, bold, 13, Left, y, text);
        canvas.SetStrokeColor(green);
        canvas.SetLineWidth(0.8);
        canvas.MoveTo(Left, y - 6);
        canvas.LineTo(Right, y - 6);
        canvas.Stroke();
    }

    private static void Label(PdfCanvas canvas, PdfCanvasFont font, PdfColor color, double x, double y, string text)
    {
        canvas.SetFillColor(color);
        Line(canvas, font, 10, x, y, text);
    }

    private static void Footer(PdfCanvas canvas, PdfCanvasFont light, PdfColor muted, int page)
    {
        canvas.SetFillColor(muted);
        Line(canvas, light, 8, Left, 36, "Riverside Community Garden is invented for the Broadside sample documents. No form is sent anywhere.");
        Line(canvas, light, 8, Right - 40, 36, $"Page {page} of 2");
    }

    private static void Line(PdfCanvas canvas, PdfCanvasFont font, double size, double x, double y, string text)
    {
        canvas.BeginText();
        canvas.SetFont(font, size);
        canvas.MoveText(x, y);
        canvas.ShowText(text);
        canvas.EndText();
    }
}

using System.Globalization;
using Broadside.Annotations;
using Broadside.Fonts;
using Broadside.Objects;
using Broadside.Writing;

namespace Broadside.Forms;

/// <summary>
/// Creates form fields: the field dictionaries (and the non-terminal ancestors a dotted name needs), their widget annotations on the
/// pages, the form's default resources and appearance, and each widget's appearance; every change is undone when any step fails.
/// </summary>
/// <remarks>
/// ISO 32000-2 §12.7.3 (Table 224: <c>Fields</c>, <c>DR</c>, <c>DA</c>, <c>SigFlags</c>), §12.7.4.1 (Table 226: <c>FT</c>,
/// <c>Parent</c>, <c>Kids</c>, <c>T</c>, <c>TU</c>, <c>Ff</c>), §12.7.4.2 (names), §12.7.4.3 (Table 228: <c>DA</c>, <c>Q</c>),
/// §12.7.5 and §12.5.6.19 (widgets, Table 192 <c>MK</c>).
/// </remarks>
internal static class FieldCreation
{
    /// <summary>The resource name the form's default font is looked for, or added, under.</summary>
    public const string HelveticaStem = "Helv";

    /// <summary>The resource name the form's ZapfDingbats font is looked for, or added, under.</summary>
    public const string ZapfDingbatsStem = "ZaDb";

    private const PdfFieldFlags CommonFlags = PdfFieldFlags.ReadOnly | PdfFieldFlags.Required | PdfFieldFlags.NoExport;

    private static readonly CosName Widget = new("Widget");
    private static readonly CosName Helvetica = new("Helvetica");
    private static readonly CosName ZapfDingbats = new("ZapfDingbats");
    private static readonly CosName SolidStyle = new("S");

    /// <summary>The kinds of terminal field the form can create.</summary>
    public enum Kind
    {
        Text,
        CheckBox,
        RadioButton,
        ComboBox,
        ListBox,
        PushButton,
        Signature,
    }

    /// <summary>A new interactive form dictionary: no fields, Helvetica and ZapfDingbats as its default resources, auto-sized black Helvetica as its default appearance.</summary>
    /// <param name="document">The document the fonts are added to.</param>
    /// <returns>The dictionary, not yet in the document.</returns>
    public static CosDictionary NewForm(PdfDocument document)
    {
        var fonts = new CosDictionary
        {
            [new CosName(HelveticaStem)] = document.AddObject(StandardFont(Helvetica)),
            [new CosName(ZapfDingbatsStem)] = document.AddObject(StandardFont(ZapfDingbats)),
        };
        return new CosDictionary
        {
            [FormNames.Fields] = new CosArray(),
            [FormNames.DR] = new CosDictionary { [FontNames.Font] = fonts },
            [FormNames.DA] = new CosString("/Helv 0 Tf 0 g"u8),
        };
    }

    /// <summary>Creates a terminal field named <paramref name="name"/> with one widget per placement and returns its view.</summary>
    /// <typeparam name="T">The field's view type.</typeparam>
    /// <param name="form">The form.</param>
    /// <param name="name">The fully qualified name: partial names joined by periods; missing ancestors are created.</param>
    /// <param name="kind">The kind of field.</param>
    /// <param name="widgets">Where the widgets go and, for radio buttons, each one's on state.</param>
    /// <param name="choices">A choice field's options; empty for other fields.</param>
    /// <param name="options">The options.</param>
    /// <returns>The new field.</returns>
    public static T Add<T>(PdfAcroForm form, string name, Kind kind, IReadOnlyList<(PdfPage Page, PdfRectangle Rectangle, CosName? State)> widgets, IReadOnlyList<PdfChoiceOption> choices, PdfFieldOptions options)
        where T : PdfTerminalField
    {
        PdfDocument document = form.Document;
        string[] parts = Split(name);
        PdfFieldFlags flags = CheckFlags(kind, options);
        CheckOptions(kind, options);
        foreach ((PdfPage page, PdfRectangle rectangle, _) in widgets)
        {
            CheckPlacement(document, page, rectangle);
        }

        var log = new MutationLog(document);
        try
        {
            (CosDictionary? parent, CosReference? parentReference) = FindOrCreateParents(form, parts, log);
            (CosName helvetica, CosName zapfDingbats) = EnsureDefaults(form, log);

            var field = new CosDictionary
            {
                [FormNames.FT] = kind switch
                {
                    Kind.Text => FormNames.Tx,
                    Kind.ComboBox or Kind.ListBox => FormNames.Ch,
                    Kind.Signature => FormNames.Sig,
                    _ => FormNames.Btn,
                },
                [FormNames.T] = new CosString(TextStringEncoder.Encode(parts[^1])),
            };
            if (flags != PdfFieldFlags.None)
            {
                field[FormNames.Ff] = new CosInteger((uint)flags);
            }

            if (options.AlternateName is { } alternate)
            {
                field[FormNames.TU] = new CosString(TextStringEncoder.Encode(alternate));
            }

            if (kind != Kind.Signature)
            {
                field[FormNames.DA] = new CosString(DefaultAppearance(kind is Kind.CheckBox or Kind.RadioButton ? zapfDingbats : helvetica, options));
            }

            if (options.Quadding != PdfTextJustification.Left && kind is Kind.Text or Kind.ComboBox or Kind.ListBox)
            {
                field[FormNames.Q] = new CosInteger((int)options.Quadding);
            }

            if (options.MaxLength is { } maxLength)
            {
                field[FormNames.MaxLen] = new CosInteger(maxLength);
            }

            if (kind is Kind.ComboBox or Kind.ListBox)
            {
                field[FormNames.Opt] = new CosArray(choices.Select(choice => choice.ExportValue == choice.DisplayText
                    ? (CosObject)new CosString(TextStringEncoder.Encode(choice.ExportValue))
                    : new CosArray([new CosString(TextStringEncoder.Encode(choice.ExportValue)), new CosString(TextStringEncoder.Encode(choice.DisplayText))])));
            }

            if (parentReference is not null)
            {
                field[FormNames.Parent] = parentReference;
            }

            CosReference fieldReference;
            if (kind != Kind.RadioButton && widgets.Count == 1)
            {
                // One widget, merged with the field (Table 226, Kids: "its contents have been merged into the field dictionary").
                AddWidgetEntries(field, widgets[0].Page, widgets[0].Rectangle, options, parent: null);
                fieldReference = log.AddObject(field);
                AddToPage(document, widgets[0].Page, fieldReference, log);
            }
            else
            {
                var kids = new CosArray();
                field[FormNames.Kids] = kids;
                fieldReference = log.AddObject(field);
                foreach ((PdfPage page, PdfRectangle rectangle, _) in widgets)
                {
                    var widget = new CosDictionary();
                    AddWidgetEntries(widget, page, rectangle, options, fieldReference);
                    CosReference widgetReference = log.AddObject(widget);
                    kids.Add(widgetReference);
                    AddToPage(document, page, widgetReference, log);
                }
            }

            log.Append(parent is null ? Fields(form, log) : (CosArray)document.Resolve(parent[FormNames.Kids]), fieldReference);
            if (kind == Kind.Signature)
            {
                long current = document.Resolve(form.Dictionary.TryGetValue(FormNames.SigFlags, out CosObject? sigFlags) ? sigFlags : null) is CosInteger integer ? integer.Value : 0;
                log.Set(form.Dictionary, FormNames.SigFlags, new CosInteger(current | (long)PdfSignatureFlags.SignaturesExist));
            }

            var created = (T)form.FindFields(name).Single(candidate => ReferenceEquals(candidate.Dictionary, field));
            GenerateAppearances(created, widgets, options);
            RecordFeatures(document, kind, flags, options);
            return created;
        }
        catch
        {
            log.Revert();
            throw;
        }
    }

    /// <summary>The partial names of <paramref name="name"/>, each non-empty.</summary>
    private static string[] Split(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string[] parts = name.Split('.');
        if (parts.Any(part => part.Length == 0))
        {
            throw new ArgumentException($"'{name}' is not a field name: partial names are joined by single periods and none is empty (ISO 32000-2 §12.7.4.2).", nameof(name));
        }

        return parts;
    }

    /// <summary>The flags the field is written with: the caller's, checked against the field's type, and those that select the type.</summary>
    private static PdfFieldFlags CheckFlags(Kind kind, PdfFieldOptions options)
    {
        (PdfFieldFlags allowed, PdfFieldFlags selecting) = kind switch
        {
            Kind.Text => (PdfFieldFlags.Multiline | PdfFieldFlags.Password | PdfFieldFlags.FileSelect | PdfFieldFlags.DoNotSpellCheck
                | PdfFieldFlags.DoNotScroll | PdfFieldFlags.Comb | PdfFieldFlags.RichText, PdfFieldFlags.None),
            Kind.CheckBox => (PdfFieldFlags.None, PdfFieldFlags.None),
            Kind.RadioButton => (PdfFieldFlags.NoToggleToOff | PdfFieldFlags.RadiosInUnison, PdfFieldFlags.Radio),
            Kind.ComboBox => (PdfFieldFlags.Edit | PdfFieldFlags.Sort | PdfFieldFlags.DoNotSpellCheck | PdfFieldFlags.CommitOnSelChange, PdfFieldFlags.Combo),
            Kind.ListBox => (PdfFieldFlags.Sort | PdfFieldFlags.MultiSelect | PdfFieldFlags.CommitOnSelChange, PdfFieldFlags.None),
            Kind.PushButton => (PdfFieldFlags.None, PdfFieldFlags.Pushbutton),
            _ => (PdfFieldFlags.None, PdfFieldFlags.None),
        };
        PdfFieldFlags flags = options.Flags;
        if ((flags & ~(allowed | CommonFlags)) is var other and not PdfFieldFlags.None)
        {
            throw new ArgumentException($"The flags {other} do not apply to a {Describe(kind)} (ISO 32000-2 §12.7.4.1, Table 227; §12.7.5).", nameof(options));
        }

        if (kind == Kind.Text && (flags & PdfFieldFlags.Comb) != 0
            && (options.MaxLength is null || (flags & (PdfFieldFlags.Multiline | PdfFieldFlags.Password | PdfFieldFlags.FileSelect)) != 0))
        {
            throw new ArgumentException("The Comb flag needs a MaxLength and the Multiline, Password and FileSelect flags clear (ISO 32000-2 §12.7.5.3, Table 231).", nameof(options));
        }

        return flags | selecting;
    }

    private static void CheckOptions(Kind kind, PdfFieldOptions options)
    {
        if (options.MaxLength is not null && kind != Kind.Text)
        {
            throw new ArgumentException($"MaxLength applies to text fields only, not to a {Describe(kind)} (ISO 32000-2 §12.7.5.3, Table 232).", nameof(options));
        }

        if (options.Caption is not null && kind is not (Kind.CheckBox or Kind.RadioButton or Kind.PushButton))
        {
            throw new ArgumentException($"A caption applies to buttons only, not to a {Describe(kind)} (ISO 32000-2 §12.5.6.19, Table 192).", nameof(options));
        }
    }

    private static string Describe(Kind kind) => kind switch
    {
        Kind.Text => "text field",
        Kind.CheckBox => "check box",
        Kind.RadioButton => "radio button field",
        Kind.ComboBox => "combo box",
        Kind.ListBox => "list box",
        Kind.PushButton => "push button",
        _ => "signature field",
    };

    /// <summary>Checks that <paramref name="page"/> is an indirect page object of <paramref name="document"/> and the rectangle is finite.</summary>
    private static void CheckPlacement(PdfDocument document, PdfPage page, PdfRectangle rectangle)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Reference is null || document.Pages.IndexOf(page) < 0 || !ReferenceEquals(document.Resolve(page.Reference), page.Dictionary))
        {
            throw new ArgumentException("The page shall be an indirect page object of the form's document.", nameof(page));
        }

        if (!double.IsFinite(rectangle.Left) || !double.IsFinite(rectangle.Bottom) || !double.IsFinite(rectangle.Right) || !double.IsFinite(rectangle.Top))
        {
            throw new ArgumentException("The widget rectangle shall be finite.", nameof(rectangle));
        }
    }

    /// <summary>
    /// The parent of the new field: the existing non-terminal field its name's ancestors name, the ancestors that are missing created
    /// below it; <see langword="null"/> for a root field.
    /// </summary>
    private static (CosDictionary? Parent, CosReference? Reference) FindOrCreateParents(PdfAcroForm form, string[] parts, MutationLog log)
    {
        string full = string.Join('.', parts);
        if (form.FindFields(full).Count > 0)
        {
            throw new InvalidOperationException($"The form already has a field named {full}; fields with the same fully qualified name are one field (ISO 32000-2 §12.7.4.2).");
        }

        CosDictionary? parent = null;
        CosReference? parentReference = null;
        bool creating = false;
        for (int depth = 1; depth < parts.Length; depth++)
        {
            string prefix = string.Join('.', parts, 0, depth);
            if (!creating)
            {
                IReadOnlyList<PdfField> existing = form.FindFields(prefix);
                if (existing.Count > 0)
                {
                    if (existing.OfType<PdfNonTerminalField>().FirstOrDefault() is not { } ancestor)
                    {
                        throw new InvalidOperationException($"The field {prefix} is a terminal field; it cannot have the child field {full} (ISO 32000-2 §12.7.4.1).");
                    }

                    if (ancestor.Reference is null || form.Document.Resolve(ancestor.Dictionary.TryGetValue(FormNames.Kids, out CosObject? kids) ? kids : null) is not CosArray)
                    {
                        throw new InvalidOperationException($"The field {prefix} is not an indirect field dictionary with a Kids array; a child cannot be added to it (ISO 32000-2 §12.7.4.1).");
                    }

                    (parent, parentReference) = (ancestor.Dictionary, ancestor.Reference);
                    continue;
                }

                creating = true;
            }

            var created = new CosDictionary
            {
                [FormNames.T] = new CosString(TextStringEncoder.Encode(parts[depth - 1])),
                [FormNames.Kids] = new CosArray(),
            };
            if (parentReference is not null)
            {
                created[FormNames.Parent] = parentReference;
            }

            CosReference reference = log.AddObject(created);
            log.Append(parent is null ? Fields(form, log) : (CosArray)form.Document.Resolve(parent[FormNames.Kids]), reference);
            (parent, parentReference) = (created, reference);
        }

        return (parent, parentReference);
    }

    /// <summary>The form's <c>Fields</c> array, made an empty one when it is missing or not an array.</summary>
    private static CosArray Fields(PdfAcroForm form, MutationLog log)
    {
        if (form.Document.Resolve(form.Dictionary.TryGetValue(FormNames.Fields, out CosObject? fields) ? fields : null) is CosArray array)
        {
            return array;
        }

        var created = new CosArray();
        log.Set(form.Dictionary, FormNames.Fields, created);
        return created;
    }

    /// <summary>
    /// The resource names of Helvetica and ZapfDingbats in the form's default resources, adding the fonts (and the <c>DR</c> and its
    /// <c>Font</c> dictionary) when they are missing, and the form's default appearance when it has none.
    /// </summary>
    private static (CosName Helvetica, CosName ZapfDingbats) EnsureDefaults(PdfAcroForm form, MutationLog log)
    {
        PdfDocument document = form.Document;
        if (document.Resolve(form.Dictionary.TryGetValue(FormNames.DR, out CosObject? dr) ? dr : null) is not CosDictionary resources)
        {
            resources = [];
            log.Set(form.Dictionary, FormNames.DR, resources);
        }

        if (document.Resolve(resources.TryGetValue(FontNames.Font, out CosObject? font) ? font : null) is not CosDictionary fonts)
        {
            fonts = [];
            log.Set(resources, FontNames.Font, fonts);
        }

        CosName helvetica = FindOrAddFont(document, fonts, Helvetica, HelveticaStem, log);
        CosName zapfDingbats = FindOrAddFont(document, fonts, ZapfDingbats, ZapfDingbatsStem, log);
        if (!form.Dictionary.ContainsKey(FormNames.DA))
        {
            log.Set(form.Dictionary, FormNames.DA, new CosString(System.Text.Encoding.ASCII.GetBytes($"/{helvetica.Value} 0 Tf 0 g")));
        }

        return (helvetica, zapfDingbats);
    }

    /// <summary>The name of the standard Type 1 font <paramref name="baseFont"/> in <paramref name="fonts"/>, added under a new name when absent.</summary>
    private static CosName FindOrAddFont(PdfDocument document, CosDictionary fonts, CosName baseFont, string stem, MutationLog log)
    {
        foreach (KeyValuePair<CosName, CosObject> entry in fonts)
        {
            if (document.Resolve(entry.Value) is CosDictionary candidate
                && FontNames.Type1.Equals(candidate.TryGetValue(AnnotationNames.Subtype, out CosObject? subtype) ? subtype : null)
                && baseFont.Equals(candidate.TryGetValue(FontNames.BaseFont, out CosObject? name) ? name : null))
            {
                return entry.Key;
            }
        }

        CosName added = TextFieldAppearance.UniqueName(fonts, stem);
        log.Set(fonts, added, log.AddObject(StandardFont(baseFont)));
        return added;
    }

    /// <summary>A standard 14 Type 1 font dictionary; Helvetica in WinAnsiEncoding (§9.6.2.2, §9.6.5.1).</summary>
    private static CosDictionary StandardFont(CosName baseFont)
    {
        var dictionary = new CosDictionary
        {
            [KnownNames.Type] = FontNames.Font,
            [AnnotationNames.Subtype] = FontNames.Type1,
            [FontNames.BaseFont] = baseFont,
        };
        if (!ZapfDingbats.Equals(baseFont))
        {
            dictionary[FontNames.Encoding] = FontNames.WinAnsiEncoding;
        }

        return dictionary;
    }

    /// <summary>The field's default appearance: <c>/font size Tf</c> and the text colour (§12.7.4.3).</summary>
    private static byte[] DefaultAppearance(CosName font, PdfFieldOptions options)
    {
        var text = new System.Text.StringBuilder();
        text.Append('/').Append(font.Value).Append(' ').Append(Format(options.FontSize)).Append(" Tf");
        foreach (double component in options.TextColor.Components)
        {
            text.Append(' ').Append(Format(component));
        }

        text.Append(options.TextColor.Components.Count switch { 1 => " g", 3 => " rg", _ => " k" });
        return System.Text.Encoding.ASCII.GetBytes(text.ToString());
    }

    private static string Format(double value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>Makes <paramref name="dictionary"/> a printable widget annotation on <paramref name="page"/> with the options' colours and border.</summary>
    private static void AddWidgetEntries(CosDictionary dictionary, PdfPage page, PdfRectangle rectangle, PdfFieldOptions options, CosReference? parent)
    {
        dictionary[KnownNames.Type] = AnnotationNames.Annot;
        dictionary[AnnotationNames.Subtype] = Widget;
        dictionary[AnnotationNames.Rect] = new CosArray([
            TextFieldAppearance.Number(rectangle.Left), TextFieldAppearance.Number(rectangle.Bottom),
            TextFieldAppearance.Number(rectangle.Right), TextFieldAppearance.Number(rectangle.Top)]);
        dictionary[AnnotationNames.F] = new CosInteger((int)PdfAnnotationFlags.Print);
        dictionary[AnnotationNames.P] = page.Reference!;
        if (parent is not null)
        {
            dictionary[FormNames.Parent] = parent;
        }

        var characteristics = new CosDictionary();
        if (options.BorderColor is { } border)
        {
            characteristics[AnnotationNames.BC] = Color(border);
            dictionary[AnnotationNames.BS] = new CosDictionary
            {
                [AnnotationNames.W] = TextFieldAppearance.Number(options.BorderWidth),
                [AnnotationNames.S] = SolidStyle,
            };
        }

        if (options.BackgroundColor is { } background)
        {
            characteristics[AnnotationNames.BG] = Color(background);
        }

        if (options.Caption is { } caption)
        {
            characteristics[AnnotationNames.NormalCaption] = new CosString(TextStringEncoder.Encode(caption));
        }

        if (characteristics.Count > 0)
        {
            dictionary[AnnotationNames.MK] = characteristics;
        }
    }

    private static CosArray Color(PdfDeviceColor color) => new(color.Components.Select(TextFieldAppearance.Number));

    /// <summary>Adds the annotation to the page's <c>Annots</c>, keeping the annotations it reads from a page tree node above it.</summary>
    private static void AddToPage(PdfDocument document, PdfPage page, CosReference annotation, MutationLog log)
    {
        if (document.Resolve(page.Dictionary.TryGetValue(AnnotationNames.Annots, out CosObject? own) ? own : null) is CosArray array)
        {
            log.Append(array, annotation);
            return;
        }

        var annotations = new CosArray();
        if (own is null && page.FindInheritedAnnots() is { } inherited && document.Resolve(inherited) is CosArray kept)
        {
            foreach (CosObject element in kept)
            {
                annotations.Add(element);
            }
        }

        annotations.Add(annotation);
        log.Set(page.Dictionary, AnnotationNames.Annots, annotations);
    }

    /// <summary>Gives every widget of the new field its normal appearance, as the field's own generation does.</summary>
    private static void GenerateAppearances(PdfTerminalField field, IReadOnlyList<(PdfPage Page, PdfRectangle Rectangle, CosName? State)> widgets, PdfFieldOptions options)
    {
        switch (field)
        {
            case PdfTextField text:
                text.GenerateAppearances();
                break;
            case PdfRadioButtonField radio:
                ToggleButtonAppearance.Build(radio, [.. widgets.Select(widget => widget.State!)]).Apply();
                break;
            case PdfToggleButtonField toggle:
                toggle.GenerateAppearances();
                break;
            case PdfChoiceField choice:
                choice.GenerateAppearances();
                break;
            case PdfPushButtonField button:
                TextFieldAppearance.BuildCaption(button, options.Caption ?? string.Empty).Apply();
                break;
            default:
                foreach (PdfWidgetAnnotation widget in field.Widgets)
                {
                    CosStream stream = TextFieldAppearance.BuildStream(field.Document, widget, defaultResources: null, fonts: [], (_, _, _) => { });
                    widget.Dictionary[AnnotationNames.AP] = new CosDictionary { [AnnotationNames.N] = field.Document.AddObject(stream) };
                }

                break;
        }
    }

    /// <summary>Records the versions the new field needs.</summary>
    private static void RecordFeatures(PdfDocument document, Kind kind, PdfFieldFlags flags, PdfFieldOptions options)
    {
        document.RequireFeature(VersionedFeatures.InteractiveForm);
        if (kind == Kind.Signature)
        {
            document.RequireFeature(VersionedFeatures.SignatureFields);
        }

        if (options.AlternateName is not null)
        {
            document.RequireFeature(VersionedFeatures.FieldAlternateName);
        }

        bool button = kind is Kind.CheckBox or Kind.RadioButton or Kind.PushButton;
        bool text = kind == Kind.Text;
        PdfFieldFlags pdf14 = PdfFieldFlags.FileSelect | PdfFieldFlags.MultiSelect | PdfFieldFlags.DoNotSpellCheck | (text ? PdfFieldFlags.DoNotScroll : 0);
        PdfFieldFlags pdf15 = PdfFieldFlags.CommitOnSelChange | (text ? PdfFieldFlags.Comb | PdfFieldFlags.RichText : 0) | (button ? PdfFieldFlags.RadiosInUnison : 0);
        if ((flags & pdf14) != 0)
        {
            document.RequireFeature(VersionedFeatures.FieldFlags14);
        }

        if ((flags & pdf15) != 0)
        {
            document.RequireFeature(VersionedFeatures.FieldFlags15);
        }
    }

    /// <summary>Changes to the document that are undone, in reverse order, when creating a field fails.</summary>
    private sealed class MutationLog(PdfDocument document)
    {
        private readonly List<Action> _undo = [];

        public CosReference AddObject(CosObject value)
        {
            CosReference reference = document.AddObject(value);
            _undo.Add(() => document.RemoveObject(reference));
            return reference;
        }

        public void Set(CosDictionary dictionary, CosName key, CosObject value)
        {
            bool had = dictionary.TryGetValue(key, out CosObject? previous);
            dictionary[key] = value;
            _undo.Add(() =>
            {
                if (had)
                {
                    dictionary[key] = previous!;
                }
                else
                {
                    dictionary.Remove(key);
                }
            });
        }

        public void Append(CosArray array, CosObject value)
        {
            array.Add(value);
            _undo.Add(() => array.RemoveAt(array.Count - 1));
        }

        public void Revert()
        {
            for (int index = _undo.Count - 1; index >= 0; index--)
            {
                _undo[index]();
            }

            _undo.Clear();
        }
    }
}

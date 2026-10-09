using System.Globalization;
using Broadside.Diagnostics;
using Broadside.Objects;
using Broadside.Parsing;

namespace Broadside.Fonts;

/// <summary>
/// The glyph name and width of each of the 256 codes of a simple font, computed once from its font dictionary, and the COS
/// containers they were computed from with their versions, so a change to any of them is noticed (pitfall: views are live).
/// </summary>
/// <remarks>Immutable once built; a font replaces it, never changes it, so concurrent readers always see a whole table.</remarks>
internal sealed class SimpleFontMetrics
{
    private readonly CosObject[] _sources;
    private readonly int[] _versions;

    private SimpleFontMetrics(string[] names, double[] widths, Standard14Font? standard14, List<CosObject> sources)
    {
        Names = names;
        Widths = widths;
        Standard14 = standard14;
        SynthesizedDescriptor = standard14 is { } font ? new PdfFontDescriptor(font) : null;
        _sources = [.. sources];
        _versions = [.. sources.Select(VersionOf)];
    }

    /// <summary>Gets the glyph name of each code; <c>.notdef</c> where the encoding gives none.</summary>
    public string[] Names { get; }

    /// <summary>Gets the width of each code, in glyph space units.</summary>
    public double[] Widths { get; }

    /// <summary>Gets the Standard 14 font whose metrics apply, if any.</summary>
    public Standard14Font? Standard14 { get; }

    /// <summary>Gets the descriptor synthesized from Standard 14 metrics, used when the font has no <c>FontDescriptor</c>.</summary>
    public PdfFontDescriptor? SynthesizedDescriptor { get; }

    /// <summary>Gets a value indicating whether no source dictionary or array has changed since the tables were built.</summary>
    public bool IsCurrent
    {
        get
        {
            for (int index = 0; index < _sources.Length; index++)
            {
                if (VersionOf(_sources[index]) != _versions[index])
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Computes the tables of a simple font, recording each deviation found on the font's object.</summary>
    /// <param name="font">The font.</param>
    /// <returns>The tables.</returns>
    public static SimpleFontMetrics Build(PdfSimpleFont font)
    {
        var builder = new Builder(font);
        return builder.Build();
    }

    private static int VersionOf(CosObject source) => source switch
    {
        CosDictionary dictionary => dictionary.Version,
        CosArray array => array.Version,
        _ => 0,
    };

    /// <summary>One computation: ISO 32000-2 §9.6.2.1 (Table 109), §9.6.2.2, §9.6.5.1 to §9.6.5.3 and §9.8.</summary>
    private sealed class Builder(PdfSimpleFont font)
    {
        private readonly List<CosObject> _sources = [font.Dictionary];
        private readonly string?[] _names = new string?[256];
        private readonly double[] _widths = new double[256];
        private CosDictionary? _descriptor;
        private bool _embedded;
        private Standard14Font? _standard14;
        private bool _symbolic;

        private bool IsType3 => font.FontType == PdfFontType.Type3;

        public SimpleFontMetrics Build()
        {
            ReadDescriptor();
            MatchStandard14();
            ReadEncoding();
            string[] names = new string[256];
            for (int code = 0; code < 256; code++)
            {
                names[code] = _names[code] ?? GlyphNameTable.NotDef;
            }

            ReadWidths(names);
            CheckStandard14Entries();
            return new SimpleFontMetrics(names, _widths, _standard14, _sources);
        }

        /// <summary>The font descriptor (§9.8): present, a dictionary, well-formed; whether it embeds a program; its flags.</summary>
        private void ReadDescriptor()
        {
            CosObject? value = font.Get(FontNames.FontDescriptor);
            if (value is CosDictionary descriptor)
            {
                _descriptor = descriptor;
                _sources.Add(descriptor);
                _embedded = font.IsEmbeddedIn(descriptor);
                CheckDescriptor(descriptor);
            }
            else if (value is not null)
            {
                font.Report(
                    DiagnosticCodes.FontDescriptorInvalid,
                    DiagnosticSeverity.Warning,
                    "A font's FontDescriptor shall be a dictionary (ISO 32000-2 §9.6.2.1, Table 109); ignored.");
            }
        }

        private void CheckDescriptor(CosDictionary descriptor)
        {
            var problems = new List<string>();
            CosObject? type = font.GetFrom(descriptor, KnownNames.Type);
            if (!FontNames.FontDescriptor.Equals(type))
            {
                problems.Add("its Type is not FontDescriptor");
            }

            if (font.GetFrom(descriptor, FontNames.Flags) is not CosInteger)
            {
                problems.Add("Flags is missing or not an integer");
            }

            CosName[] required = IsType3
                ? [FontNames.ItalicAngle]
                : [FontNames.FontBBox, FontNames.ItalicAngle, FontNames.Ascent, FontNames.Descent, FontNames.StemV];
            foreach (CosName key in required)
            {
                CosObject? entry = font.GetFrom(descriptor, key);
                bool valid = FontNames.FontBBox.Equals(key) ? entry is CosArray { Count: 4 } : entry is CosNumber;
                if (!valid)
                {
                    problems.Add($"{key.Value} is missing or malformed");
                }
            }

            int programs = (font.GetFrom(descriptor, FontNames.FontFile) is null ? 0 : 1)
                + (font.GetFrom(descriptor, FontNames.FontFile2) is null ? 0 : 1)
                + (font.GetFrom(descriptor, FontNames.FontFile3) is null ? 0 : 1);
            if (programs > 1)
            {
                problems.Add("it has more than one of FontFile, FontFile2 and FontFile3");
            }

            if (problems.Count > 0)
            {
                font.Report(
                    DiagnosticCodes.FontDescriptorInvalid,
                    DiagnosticSeverity.Warning,
                    $"The font descriptor deviates from ISO 32000-2 §9.8.1 Table 120: {string.Join("; ", problems)}; defaults are used.");
            }
        }

        /// <summary>A Type 1 or TrueType font that is not embedded and is named after a Standard 14 font takes its metrics (§9.6.2.2).</summary>
        private void MatchStandard14()
        {
            CosObject? baseFont = font.Get(FontNames.BaseFont);
            if (baseFont is not CosName name)
            {
                if (!IsType3)
                {
                    font.Report(
                        DiagnosticCodes.FontBaseFontMissing,
                        DiagnosticSeverity.Warning,
                        "A font dictionary shall have a BaseFont name (ISO 32000-2 §9.6.2.1, Table 109).");
                }
            }
            else if (font.CanUseStandard14 && !_embedded && Standard14Data.TryMatch(name.Value, out Standard14Font standard14, out bool isAlias))
            {
                _standard14 = standard14;
                if (isAlias)
                {
                    font.Report(
                        DiagnosticCodes.FontStandard14Alias,
                        DiagnosticSeverity.Information,
                        $"The font /{name.Value} is not embedded; the metrics of the Standard 14 font {Standard14Data.PostScriptName(standard14)} are used for it.");
                }
            }

            // §9.8.2: a processor checks the Symbolic flag. Without a descriptor, a Standard 14 font's flags come from its AFM file.
            PdfFontFlags flags = _descriptor is { } descriptor
                ? PdfFontDescriptor.ReadFlags(descriptor, font.Document)
                : _standard14 is { } metrics ? Standard14Data.Flags(metrics) : PdfFontFlags.None;
            _symbolic = flags.HasFlag(PdfFontFlags.Symbolic);
        }

        /// <summary>The encoding: §9.6.5.1 Table 112, §9.6.5.2 for Type 1 and others, §9.6.5.3 for Type 3.</summary>
        private void ReadEncoding()
        {
            CosObject? encoding = font.Get(FontNames.Encoding);
            if (IsType3)
            {
                ReadType3Encoding(encoding);
                return;
            }

            bool namedEncodingIgnored = !_embedded && _standard14 is { } symbolFont && Standard14Data.IsSymbolic(symbolFont);
            switch (encoding)
            {
                case null:
                    ApplyBuiltIn();
                    break;
                case CosName name:
                    if (namedEncodingIgnored)
                    {
                        ReportNamedEncodingIgnored(name);
                        ApplyBuiltIn();
                    }
                    else if (ReadPredefined(name) is { } predefined)
                    {
                        Apply(predefined);
                    }
                    else
                    {
                        ApplyBuiltIn();
                    }

                    break;
                case CosDictionary dictionary:
                    _sources.Add(dictionary);
                    BuiltInEncoding? baseEncoding = null;
                    if (font.GetFrom(dictionary, FontNames.BaseEncoding) is { } baseValue)
                    {
                        if (baseValue is not CosName baseName)
                        {
                            ReportEncodingInvalid("an encoding dictionary's BaseEncoding is not a name");
                        }
                        else if (namedEncodingIgnored)
                        {
                            ReportNamedEncodingIgnored(baseName);
                        }
                        else
                        {
                            baseEncoding = ReadPredefined(baseName);
                        }
                    }

                    // Table 112: without BaseEncoding, an embedded program's built-in encoding; otherwise StandardEncoding for a
                    // nonsymbolic font and the font's built-in encoding for a symbolic one.
                    if (baseEncoding is { } explicitBase)
                    {
                        Apply(explicitBase);
                    }
                    else if (_embedded || _symbolic)
                    {
                        ApplyBuiltIn();
                    }
                    else
                    {
                        Apply(BuiltInEncoding.Standard);
                    }

                    ApplyDifferences(dictionary);
                    break;
                default:
                    ReportEncodingInvalid("Encoding is neither a name nor a dictionary");
                    ApplyBuiltIn();
                    break;
            }
        }

        /// <summary>§9.6.5.3: a Type 3 font's encoding is entirely its Encoding entry, which is required.</summary>
        private void ReadType3Encoding(CosObject? encoding)
        {
            switch (encoding)
            {
                case CosDictionary dictionary:
                    _sources.Add(dictionary);
                    if (font.GetFrom(dictionary, FontNames.BaseEncoding) is CosName baseName && ReadPredefined(baseName) is { } baseEncoding)
                    {
                        Apply(baseEncoding);
                    }

                    ApplyDifferences(dictionary);
                    break;
                case CosName name:
                    ReportEncodingInvalid("a Type 3 font's Encoding shall be an encoding dictionary (§9.6.4, Table 110), not a name");
                    if (ReadPredefined(name) is { } predefined)
                    {
                        Apply(predefined);
                    }

                    break;
                default:
                    ReportEncodingInvalid("a Type 3 font shall have an encoding dictionary (§9.6.4, Table 110); no code has a glyph");
                    break;
            }
        }

        /// <summary>The three predefined encodings (Table 112); StandardEncoding is accepted with a diagnostic, other names are not.</summary>
        private BuiltInEncoding? ReadPredefined(CosName name)
        {
            if (FontNames.WinAnsiEncoding.Equals(name))
            {
                return BuiltInEncoding.WinAnsi;
            }

            if (FontNames.MacRomanEncoding.Equals(name))
            {
                return BuiltInEncoding.MacRoman;
            }

            if (FontNames.MacExpertEncoding.Equals(name))
            {
                return BuiltInEncoding.MacExpert;
            }

            if (FontNames.StandardEncoding.Equals(name))
            {
                ReportEncodingInvalid("/StandardEncoding is not a predefined encoding name (Annex D.1); StandardEncoding is used");
                return BuiltInEncoding.Standard;
            }

            ReportEncodingInvalid($"/{name.Value} is not one of MacRomanEncoding, MacExpertEncoding and WinAnsiEncoding; the font's built-in encoding is used");
            return null;
        }

        /// <summary>
        /// The font's built-in encoding: the program's when embedded and read; the Symbol or ZapfDingbats encoding for those fonts;
        /// StandardEncoding otherwise (a non-embedded font without a substitute: no other built-in encoding is known).
        /// </summary>
        private void ApplyBuiltIn()
        {
            if (_embedded)
            {
                if (font.GetProgramEncoding() is { } program)
                {
                    for (int code = 0; code < 256; code++)
                    {
                        _names[code] = program[code];
                    }

                    return;
                }

                font.Report(
                    DiagnosticCodes.FontBuiltInEncodingUnavailable,
                    DiagnosticSeverity.Information,
                    "The embedded font program's built-in encoding is not read; StandardEncoding stands in for it.");
                Apply(BuiltInEncoding.Standard);
                return;
            }

            if (_standard14 is { } standard14)
            {
                Apply(Standard14Data.EncodingOf(standard14));
                return;
            }

            if (_symbolic)
            {
                font.Report(
                    DiagnosticCodes.FontBuiltInEncodingUnavailable,
                    DiagnosticSeverity.Information,
                    "A symbolic font that is not embedded has no known built-in encoding (ISO 32000-2 §9.6.5.1); StandardEncoding stands in for it.");
            }

            Apply(BuiltInEncoding.Standard);
        }

        private void Apply(BuiltInEncoding encoding)
        {
            ReadOnlySpan<short> table = GlyphNameTable.Table(encoding);
            for (int code = 0; code < 256; code++)
            {
                _names[code] = table[code] >= 0 ? GlyphNameTable.Names[table[code]] : null;
            }
        }

        /// <summary>§9.6.5.1: <c>code name name … code name …</c>; each name takes the current code, which then moves on.</summary>
        private void ApplyDifferences(CosDictionary encoding)
        {
            CosObject? value = font.GetFrom(encoding, FontNames.Differences);
            if (value is null)
            {
                return;
            }

            if (value is not CosArray differences)
            {
                ReportDifferencesInvalid("Differences is not an array; ignored");
                return;
            }

            _sources.Add(differences);
            int code = -1;
            foreach (CosObject item in differences)
            {
                switch (font.Document.Resolve(item))
                {
                    case CosInteger integer:
                        code = integer.Value is >= int.MinValue and <= int.MaxValue ? (int)integer.Value : -1;
                        break;
                    case CosReal real when real.Value == Math.Floor(real.Value) && Math.Abs(real.Value) < int.MaxValue:
                        code = (int)real.Value;
                        break;
                    case CosName name:
                        if (code is >= 0 and <= 255)
                        {
                            // The empty name (a lone slash) can name no glyph: the code shows .notdef.
                            _names[code] = name.Bytes.IsEmpty ? null : name.Value;
                        }
                        else
                        {
                            ReportDifferencesInvalid(string.Create(CultureInfo.InvariantCulture, $"a name is given for code {code}, outside 0 to 255; ignored"));
                        }

                        if (code >= 0)
                        {
                            code++;
                        }

                        break;
                    default:
                        ReportDifferencesInvalid("an element is neither a code nor a name; skipped");
                        break;
                }
            }
        }

        /// <summary>§9.6.2.1 Table 109: Widths from FirstChar to LastChar, MissingWidth outside; Standard 14 metrics when absent.</summary>
        private void ReadWidths(string[] names)
        {
            CosObject? value = font.Get(FontNames.Widths);
            if (value is CosArray widths)
            {
                _sources.Add(widths);
                ReadWidthsArray(widths, names);
                return;
            }

            if (value is not null)
            {
                font.Report(
                    DiagnosticCodes.FontWidthsInvalid,
                    DiagnosticSeverity.Warning,
                    "A font's Widths shall be an array (ISO 32000-2 §9.6.2.1, Table 109); ignored.");
            }

            if (_standard14 is { } standard14)
            {
                for (int code = 0; code < 256; code++)
                {
                    _widths[code] = Standard14Width(standard14, names[code]);
                }

                return;
            }

            font.Report(
                DiagnosticCodes.FontWidthsMissing,
                DiagnosticSeverity.Warning,
                "The font has no Widths and is not a Standard 14 font (ISO 32000-2 §9.6.2.1, Table 109); every width is 0.");
        }

        private void ReadWidthsArray(CosArray widths, string[] names)
        {
            int count = widths.Count;
            int first;
            if (font.Get(FontNames.FirstChar) is CosInteger { Value: >= 0 and <= 255 } firstChar)
            {
                first = (int)firstChar.Value;
            }
            else
            {
                first = 0;
                ReportWidthsInvalid("FirstChar is missing or not a code from 0 to 255; 0 is used");
            }

            int last;
            if (font.Get(FontNames.LastChar) is CosInteger { Value: >= 0 and <= 255 } lastChar && lastChar.Value >= first)
            {
                last = (int)lastChar.Value;
                if (last - first + 1 != count)
                {
                    ReportWidthsInvalid(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Widths has {count} elements, but LastChar - FirstChar + 1 is {last - first + 1}; codes without an element have MissingWidth"));
                }
            }
            else
            {
                last = Math.Min(255, first + count - 1);
                ReportWidthsInvalid("LastChar is missing or malformed; FirstChar plus the number of widths is used");
            }

            // A code outside the range has the descriptor's MissingWidth (default 0); with no descriptor, a Standard 14 font's metrics.
            double? missingWidth = _descriptor is { } descriptor
                ? font.GetFrom(descriptor, FontNames.MissingWidth) is CosNumber missing ? missing.ToDouble() : 0
                : _standard14 is null ? 0 : null;
            for (int code = 0; code < 256; code++)
            {
                int index = code - first;
                if (code >= first && code <= last && index < count)
                {
                    if (font.Document.Resolve(widths[index]) is CosNumber width)
                    {
                        _widths[code] = width.ToDouble();
                        continue;
                    }

                    ReportWidthsInvalid("an element of Widths is not a number; MissingWidth is used for its code");
                }

                _widths[code] = missingWidth ?? Standard14Width(_standard14!.Value, names[code]);
            }
        }

        private double Standard14Width(Standard14Font standard14, string glyphName)
        {
            if (Standard14Data.TryGetWidth(standard14, glyphName, out double width))
            {
                return width;
            }

            font.Report(
                DiagnosticCodes.FontGlyphMissing,
                DiagnosticSeverity.Information,
                $"The Standard 14 font {Standard14Data.PostScriptName(standard14)} has no glyph /{glyphName}; its width is 0.");
            return 0;
        }

        /// <summary>
        /// §9.6.2.1: a Standard 14 font has all four of FirstChar, LastChar, Widths and FontDescriptor or none; any other font that
        /// is not Type 3 shall have a descriptor (§9.6.1).
        /// </summary>
        private void CheckStandard14Entries()
        {
            if (_standard14 is not null)
            {
                int present = (font.Get(FontNames.FirstChar) is null ? 0 : 1) + (font.Get(FontNames.LastChar) is null ? 0 : 1)
                    + (font.Get(FontNames.Widths) is null ? 0 : 1) + (font.Get(FontNames.FontDescriptor) is null ? 0 : 1);
                if (present is > 0 and < 4)
                {
                    font.Report(
                        DiagnosticCodes.FontStandard14EntriesIncomplete,
                        DiagnosticSeverity.Warning,
                        "A Standard 14 font shall have all or none of FirstChar, LastChar, Widths and FontDescriptor (ISO 32000-2 §9.6.2.1, Table 109); the entries present are used.");
                }
            }
            else if (!IsType3 && _descriptor is null)
            {
                font.Report(
                    DiagnosticCodes.FontDescriptorMissing,
                    DiagnosticSeverity.Warning,
                    "A simple font that is not a Standard 14 font shall have a font descriptor (ISO 32000-2 §9.6.1, §9.6.2.1).");
            }
        }

        private void ReportNamedEncodingIgnored(CosName name) => font.Report(
            DiagnosticCodes.FontEncodingIgnored,
            DiagnosticSeverity.Information,
            $"The encoding /{name.Value} is ignored for the non-embedded symbolic font; its built-in encoding is used (as pdf.js does).");

        private void ReportEncodingInvalid(string problem) => font.Report(
            DiagnosticCodes.FontEncodingInvalid,
            DiagnosticSeverity.Warning,
            $"The font's encoding deviates from ISO 32000-2 §9.6.5.1 Table 112: {problem}.");

        private void ReportDifferencesInvalid(string problem) => font.Report(
            DiagnosticCodes.FontDifferencesInvalid,
            DiagnosticSeverity.Warning,
            $"The encoding's Differences array deviates from ISO 32000-2 §9.6.5.1: {problem}.");

        private void ReportWidthsInvalid(string problem) => font.Report(
            DiagnosticCodes.FontWidthsInvalid,
            DiagnosticSeverity.Warning,
            $"The font's widths deviate from ISO 32000-2 §9.6.2.1 Table 109: {problem}.");
    }
}

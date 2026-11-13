using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Broadside.Tools.ConformanceCheck;

/// <summary>Where the citation check finds each package's XML documentation.</summary>
/// <param name="Configuration">The build configuration whose output is read, e.g. <c>debug</c>: the file is <c>artifacts/bin/&lt;package&gt;/&lt;configuration&gt;/&lt;package&gt;.xml</c>.</param>
/// <param name="DocumentationDirectory">When set, every package's file is <c>&lt;directory&gt;/&lt;package&gt;.xml</c> instead (a test's output directory, for instance).</param>
public sealed record CitationOptions(string Configuration = "debug", string? DocumentationDirectory = null)
{
    /// <summary>The XML documentation file of <paramref name="package"/> under <paramref name="repositoryRoot"/>.</summary>
    public string DocumentationPath(string repositoryRoot, string package) => DocumentationDirectory is null
        ? Path.Combine(repositoryRoot, "artifacts", "bin", package, Configuration.ToLowerInvariant(), package + ".xml")
        : Path.Combine(DocumentationDirectory, package + ".xml");
}

/// <summary>
/// The reviewed rules of the citation check, read from <c>docs/conformance/citation-policy.json</c>: the citation forms that count,
/// which conformance map cites which document, which packages are checked, and the exempt types and namespaces with their reasons.
/// </summary>
public sealed class CitationPolicy
{
    /// <summary>The policy's path relative to the repository root.</summary>
    public const string RelativePath = "docs/conformance/citation-policy.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>The forms a citation may take; a type cites a clause when its documentation matches one of them.</summary>
    [JsonPropertyName("citationForms")]
    public IReadOnlyList<CitationForm> CitationForms { get; init; } = [];

    /// <summary>For each conformance map, the pattern that names its document in a citation.</summary>
    [JsonPropertyName("maps")]
    public IReadOnlyList<MapDocument> Maps { get; init; } = [];

    /// <summary>Every package under <c>src/</c>, checked or exempt.</summary>
    [JsonPropertyName("packages")]
    public IReadOnlyList<PackagePolicy> Packages { get; init; } = [];

    /// <summary>Public types, nested types and namespaces of checked packages that need no citation, each with its reason.</summary>
    [JsonPropertyName("exemptions")]
    public IReadOnlyList<CitationExemption> Exemptions { get; init; } = [];

    /// <summary>Reads the policy under <paramref name="repositoryRoot"/>.</summary>
    /// <exception cref="FileNotFoundException">The repository has no policy file.</exception>
    /// <exception cref="InvalidDataException">The file is not a valid policy.</exception>
    public static CitationPolicy Load(string repositoryRoot)
    {
        string path = Path.Combine(repositoryRoot, RelativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No {RelativePath} under '{repositoryRoot}'.", path);
        }

        CitationPolicy? policy;
        try
        {
            policy = JsonSerializer.Deserialize<CitationPolicy>(File.ReadAllText(path), SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{RelativePath} is not valid JSON: {ex.Message}", ex);
        }

        return policy ?? throw new InvalidDataException($"{RelativePath} is empty.");
    }
}

/// <summary>One accepted citation form.</summary>
/// <param name="Name">What the form is, for the reader of the policy.</param>
/// <param name="Pattern">A .NET regular expression matched against the documentation text with whitespace collapsed.</param>
public sealed record CitationForm(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("pattern")] string Pattern);

/// <summary>The document a conformance map covers, as citations name it.</summary>
/// <param name="Map">The map's file name, e.g. <c>iso-32000-2.md</c>.</param>
/// <param name="Name">The document's name in messages, e.g. <c>ISO 32000-2</c>.</param>
/// <param name="Document">A .NET regular expression matching the document's name in a citation, e.g. <c>ISO 32000-2</c>.</param>
public sealed record MapDocument(
    [property: JsonPropertyName("map")] string Map,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("document")] string Document);

/// <summary>Whether a package's public types must cite.</summary>
/// <param name="Name">The project name under <c>src/</c>.</param>
/// <param name="Checked">Whether its public types are checked; an unchecked package needs a <paramref name="Reason"/>.</param>
/// <param name="Reason">Why an unchecked package implements no specification concept.</param>
public sealed record PackagePolicy(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("checked")] bool Checked,
    [property: JsonPropertyName("reason")] string? Reason = null);

/// <summary>A public type (<c>Namespace.Type</c>, nested types as <c>Namespace.Outer.Inner</c>) or a namespace (<c>Namespace.*</c>) that needs no citation.</summary>
/// <param name="Symbol">The type's documentation name without <c>T:</c>, or a namespace followed by <c>.*</c>.</param>
/// <param name="Reason">Why it implements no specification concept.</param>
public sealed record CitationExemption(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("reason")] string Reason)
{
    /// <summary>Whether <see cref="Symbol"/> names a namespace.</summary>
    public bool IsNamespace => Symbol.EndsWith(".*", StringComparison.Ordinal);

    /// <summary>Whether this exemption covers the type <paramref name="typeName"/> (documentation name without <c>T:</c>).</summary>
    public bool Covers(string typeName) => IsNamespace
        ? typeName.StartsWith(Symbol[..^1], StringComparison.Ordinal)
        : string.Equals(typeName, Symbol, StringComparison.Ordinal) || typeName.StartsWith(Symbol + ".", StringComparison.Ordinal);
}

/// <summary>One public type: its documentation name (<c>Namespace.Outer`1.Inner</c>) and where the API file declares it.</summary>
/// <param name="Name">The documentation name without <c>T:</c>.</param>
/// <param name="File">Repository-relative path of the API file.</param>
/// <param name="Line">1-based line in that file.</param>
public sealed record PublicType(string Name, string File, int Line);

/// <summary>A package's public types as its <c>PublicAPI.Shipped.txt</c> and <c>PublicAPI.Unshipped.txt</c> declare them.</summary>
public static partial class PublicApiReader
{
    /// <summary>The public types declared by <c>src/&lt;package&gt;/PublicAPI.*.txt</c> under <paramref name="repositoryRoot"/>.</summary>
    public static IReadOnlyList<PublicType> Read(string repositoryRoot, string package)
    {
        var types = new List<PublicType>();
        foreach (string fileName in (string[])["PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt"])
        {
            string relative = $"src/{package}/{fileName}";
            string path = Path.Combine(repositoryRoot, "src", package, fileName);
            if (!File.Exists(path))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string? name = ParseTypeLine(lines[i]);
                if (name is not null)
                {
                    types.Add(new PublicType(name, relative, i + 1));
                }
            }
        }

        return types;
    }

    /// <summary>
    /// The documentation name of the type a public API line declares, or <see langword="null"/> when the line declares a member, is a
    /// comment or directive, or removes an API. Type lines carry no <c>-&gt;</c> and no parameter list: <c>Broadside.Cos.CosArray</c>,
    /// <c>abstract Broadside.Rendering.Backend&lt;TSurface&gt;</c>.
    /// </summary>
    public static string? ParseTypeLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        string text = line.Trim();
        if (text.Length == 0 || text.StartsWith('#') || text.StartsWith("*REMOVED*", StringComparison.Ordinal) || text.Contains("->", StringComparison.Ordinal) || text.Contains('(', StringComparison.Ordinal))
        {
            return null;
        }

        text = Modifiers().Replace(text, string.Empty);
        if (text.StartsWith('~'))
        {
            text = text[1..];
        }

        // Outer<T>.Inner<U, V> → Outer`1.Inner`2: replace innermost type argument lists until none is left.
        while (text.Contains('<', StringComparison.Ordinal))
        {
            string replaced = TypeArguments().Replace(text, static match => "`" + (match.Groups[1].Value.Count(static c => c == ',') + 1).ToString(CultureInfo.InvariantCulture));
            if (string.Equals(replaced, text, StringComparison.Ordinal))
            {
                return null;
            }

            text = replaced;
        }

        return text.Contains(' ', StringComparison.Ordinal) ? null : text;
    }

    [GeneratedRegex(@"^(?:(?:abstract|static|sealed|virtual|override|readonly|const|new|partial)\s+)+")]
    private static partial Regex Modifiers();

    [GeneratedRegex(@"<([^<>]*)>")]
    private static partial Regex TypeArguments();
}

/// <summary>The documentation text of one package's XML documentation file, by documentation ID.</summary>
public sealed partial class DocumentationIndex
{
    private readonly Dictionary<string, string> _members = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _typesBySimpleName = new(StringComparer.Ordinal);
    private Dictionary<string, List<string>>? _byType;

    /// <summary>Adds every member of the XML documentation file at <paramref name="path"/>.</summary>
    public void Add(string path)
    {
        XDocument document = XDocument.Load(path);
        foreach (XElement member in document.Descendants("member"))
        {
            string? id = (string?)member.Attribute("name");
            if (id is null)
            {
                continue;
            }

            string text = Whitespace().Replace(member.Value, " ").Trim();
            _byType = null;
            _members[id] = _members.TryGetValue(id, out string? existing) ? existing + " " + text : text;
            if (id.StartsWith("T:", StringComparison.Ordinal))
            {
                string simple = SimpleName(id[2..]);
                if (!_typesBySimpleName.TryGetValue(simple, out List<string>? types))
                {
                    types = [];
                    _typesBySimpleName.Add(simple, types);
                }

                types.Add(id[2..]);
            }
        }
    }

    /// <summary>The documentation text of the type <paramref name="typeName"/> (without <c>T:</c>), or <see langword="null"/> when it has none.</summary>
    public string? TypeText(string typeName) => _members.TryGetValue("T:" + typeName, out string? text) ? text : null;

    /// <summary>The documented types whose simple name (without namespace, enclosing types or arity) is <paramref name="simpleName"/>.</summary>
    public IReadOnlyList<string> TypesNamed(string simpleName) => _typesBySimpleName.TryGetValue(simpleName, out List<string>? types) ? types : [];

    /// <summary>The documentation text of the type <paramref name="typeName"/> and of every member and nested type it declares.</summary>
    public IReadOnlyList<string> TypeAndMemberTexts(string typeName)
    {
        if (_byType is null)
        {
            _byType = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach ((string id, string text) in _members)
            {
                // "M:Ns.Type.Method(Ns.Arg)" is declared by "Ns.Type"; "T:Ns.Outer.Inner" by itself. Every enclosing name collects the text too.
                string name = id[(id.IndexOf(':', StringComparison.Ordinal) + 1)..];
                int parameters = name.IndexOf('(', StringComparison.Ordinal);
                if (parameters >= 0)
                {
                    name = name[..parameters];
                }

                if (!id.StartsWith("T:", StringComparison.Ordinal))
                {
                    int dot = name.LastIndexOf('.');
                    name = dot < 0 ? string.Empty : name[..dot];
                }

                while (name.Length > 0)
                {
                    if (!_byType.TryGetValue(name, out List<string>? texts))
                    {
                        texts = [];
                        _byType.Add(name, texts);
                    }

                    texts.Add(text);
                    int dot = name.LastIndexOf('.');
                    name = dot < 0 ? string.Empty : name[..dot];
                }
            }
        }

        return _byType.TryGetValue(typeName, out List<string>? found) ? found : [];
    }

    /// <summary>The simple name of a documentation type name: <c>Broadside.Cos.CosArray`1</c> → <c>CosArray</c>.</summary>
    public static string SimpleName(string typeName)
    {
        ArgumentNullException.ThrowIfNull(typeName);

        string last = typeName[(typeName.LastIndexOf('.') + 1)..];
        int tick = last.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? last : last[..tick];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

/// <summary>
/// Checks clause citations: every public type of a checked package cites a specification in one of the policy's forms unless it is
/// exempt, and every implementing type a conformance row names is documented and cites that row's clause of the map's document.
/// </summary>
public static partial class CitationChecker
{
    /// <summary>Checks the public types of every checked package; returns one violation per uncited type and per policy problem.</summary>
    /// <param name="repositoryRoot">The directory holding <c>src/</c> and <c>docs/conformance/</c>.</param>
    /// <param name="policy">The citation policy.</param>
    /// <param name="options">Where the XML documentation is.</param>
    /// <param name="documentation">Receives the documentation of every checked package.</param>
    public static IReadOnlyList<Violation> CheckPublicTypes(string repositoryRoot, CitationPolicy policy, CitationOptions options, DocumentationIndex documentation)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(documentation);

        var violations = new List<Violation>();
        string[] policyLines = File.ReadAllLines(Path.Combine(repositoryRoot, CitationPolicy.RelativePath));
        void ReportPolicy(string search, string message) => violations.Add(new Violation(CitationPolicy.RelativePath, LineOf(policyLines, search), message));

        Regex[] forms;
        try
        {
            forms = [.. policy.CitationForms.Select(static form => new Regex(form.Pattern, RegexOptions.CultureInvariant))];
        }
        catch (ArgumentException ex)
        {
            ReportPolicy("citationForms", $"a citation form is not a valid regular expression: {ex.Message}");
            return violations;
        }

        if (forms.Length == 0)
        {
            ReportPolicy("citationForms", "the policy lists no citation forms");
        }

        string[] packages = Directory.Exists(Path.Combine(repositoryRoot, "src"))
            ? [.. Directory.GetDirectories(Path.Combine(repositoryRoot, "src")).Where(static directory => Directory.GetFiles(directory, "*.csproj").Length > 0).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
            : [];
        var policies = new Dictionary<string, PackagePolicy>(StringComparer.Ordinal);
        foreach (PackagePolicy package in policy.Packages)
        {
            if (!policies.TryAdd(package.Name, package))
            {
                ReportPolicy($"\"{package.Name}\"", $"package '{package.Name}' is listed twice");
            }
            else if (!packages.Contains(package.Name, StringComparer.Ordinal))
            {
                ReportPolicy($"\"{package.Name}\"", $"package '{package.Name}' has no project under src/");
            }
            else if (!package.Checked && string.IsNullOrWhiteSpace(package.Reason))
            {
                ReportPolicy($"\"{package.Name}\"", $"unchecked package '{package.Name}' must say why in its reason");
            }
        }

        foreach (string package in packages.Where(package => !policies.ContainsKey(package)))
        {
            ReportPolicy("\"packages\"", $"package '{package}' under src/ is not listed in the policy's packages: list it as checked, or unchecked with a reason");
        }

        foreach (CitationExemption exemption in policy.Exemptions.Where(static exemption => string.IsNullOrWhiteSpace(exemption.Reason)))
        {
            ReportPolicy($"\"{exemption.Symbol}\"", $"exemption '{exemption.Symbol}' must say why in its reason");
        }

        var usedExemptions = new HashSet<CitationExemption>();
        foreach (string package in packages)
        {
            string path = options.DocumentationPath(repositoryRoot, package);
            bool isChecked = policies.TryGetValue(package, out PackagePolicy? packagePolicy) && packagePolicy.Checked;
            if (!File.Exists(path))
            {
                if (isChecked)
                {
                    violations.Add(new Violation(
                        $"src/{package}",
                        0,
                        $"no XML documentation at '{Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/')}': build the package (dotnet build src/{package}) before checking citations"));
                }

                continue;
            }

            if (!isChecked)
            {
                continue;
            }

            documentation.Add(path);

            foreach (PublicType type in PublicApiReader.Read(repositoryRoot, package))
            {
                CitationExemption? exemption = policy.Exemptions.FirstOrDefault(exemption => exemption.Covers(type.Name));
                if (exemption is not null)
                {
                    usedExemptions.Add(exemption);
                    continue;
                }

                if (!IsCited(type.Name, documentation, forms))
                {
                    violations.Add(new Violation(
                        type.File,
                        type.Line,
                        $"public type '{type.Name}' cites no specification: add <remarks>ISO 32000-2 §x.y.z</remarks> (or another form in {CitationPolicy.RelativePath}), or exempt it there with a reason"));
                }
            }
        }

        foreach (CitationExemption exemption in policy.Exemptions.Where(exemption => !usedExemptions.Contains(exemption)))
        {
            ReportPolicy($"\"{exemption.Symbol}\"", $"exemption '{exemption.Symbol}' matches no public type of a checked package; remove it");
        }

        return violations;
    }

    /// <summary>
    /// Checks that each implementing type of each row of <paramref name="file"/> is documented and cites the row's clause of the map's document,
    /// in its own documentation or that of one of its members.
    /// </summary>
    /// <param name="file">A parsed conformance map.</param>
    /// <param name="policy">The citation policy, which names the map's document.</param>
    /// <param name="index">The source index, which says which package declares each type.</param>
    /// <param name="documentation">The documentation of every checked package.</param>
    /// <param name="violations">Receives one entry per type that is undocumented or does not cite the row's clause.</param>
    public static void CheckRows(ConformanceFile file, CitationPolicy policy, CodeIndex index, DocumentationIndex documentation, ICollection<Violation> violations)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(documentation);
        ArgumentNullException.ThrowIfNull(violations);

        string mapName = Path.GetFileName(file.File);
        MapDocument? map = policy.Maps.FirstOrDefault(map => string.Equals(map.Map, mapName, StringComparison.Ordinal));
        if (map is null)
        {
            violations.Add(new Violation(file.File, 1, $"the map is not listed in {CitationPolicy.RelativePath} 'maps', so its rows' citations cannot be checked"));
            return;
        }

        var checkedPackages = policy.Packages.Where(static package => package.Checked).Select(static package => package.Name).ToHashSet(StringComparer.Ordinal);
        Regex document;
        Regex anyDocument;
        try
        {
            document = new Regex(map.Document, RegexOptions.CultureInvariant);
            anyDocument = new Regex(
                string.Join("|", policy.Maps.Select(static map => "(?:" + map.Document + ")").Concat(policy.CitationForms.Select(static form => "(?:" + form.Pattern + ")"))),
                RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            violations.Add(new Violation(file.File, 1, $"a document pattern in {CitationPolicy.RelativePath} is not a valid regular expression: {ex.Message}"));
            return;
        }
        foreach (ConformanceRow row in file.Rows)
        {
            if (row.ImplementingTypes.Length == 0)
            {
                continue;
            }

            string clause = NormalizeClause(row.Clause);
            foreach (Match cell in Backticked().Matches(row.ImplementingTypes))
            {
                string simpleName = cell.Groups[1].Value.Trim();
                if (!index.SourceTypePackages(simpleName).Any(checkedPackages.Contains))
                {
                    // Declared only in an unchecked package (a backend or the viewer), whose types need not cite.
                    continue;
                }

                IReadOnlyList<string> candidates = documentation.TypesNamed(simpleName);
                if (candidates.Count == 0)
                {
                    violations.Add(new Violation(row.File, row.Line, $"implementing type '{simpleName}' has no XML documentation, so it cannot cite clause {row.Clause}"));
                    continue;
                }

                bool cites = candidates.Any(candidate => documentation.TypeAndMemberTexts(candidate).Any(text => CitesClause(text, document, anyDocument, clause)));
                if (!cites)
                {
                    violations.Add(new Violation(
                        row.File,
                        row.Line,
                        $"implementing type '{simpleName}' does not cite clause {row.Clause} of {map.Name} in its XML documentation or its members'"));
                }
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="text"/> cites <paramref name="clause"/> (normalized, see <see cref="NormalizeClause"/>) of the document
    /// <paramref name="document"/> matches: a clause reference (<c>§7.3.4</c>, <c>Annex D</c>, <c>clause 9</c>, or a bare clause right
    /// after the document's name) that follows the document's name before any other document is named, and equals the clause, one
    /// of its sub-clauses or, for a clause at the third level or deeper, its parent.
    /// </summary>
    public static bool CitesClause(string text, Regex document, Regex anyDocument, string clause)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(anyDocument);
        ArgumentNullException.ThrowIfNull(clause);

        // A row at the third level or deeper is also cited by its parent clause: "§8.9.5" covers the row "8.9.5.1 General".
        int lastDot = clause.LastIndexOf('.');
        string? parent = lastDot > 0 && clause.IndexOf('.', StringComparison.Ordinal) < lastDot ? clause[..lastDot] : null;
        foreach (Match name in document.Matches(text))
        {
            int start = name.Index + name.Length;
            Match next = anyDocument.Match(text, start);
            string segment = next.Success ? text[start..next.Index] : text[start..];
            foreach (Match reference in ClauseReference().Matches(segment))
            {
                string cited = NormalizeClause(reference.Groups["clause"].Value);
                if (string.Equals(cited, clause, StringComparison.Ordinal)
                    || cited.StartsWith(clause + ".", StringComparison.Ordinal)
                    || string.Equals(cited, parent, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>A clause as rows and citations compare it: <c>Annex D</c> → <c>D</c>, trailing dots dropped.</summary>
    public static string NormalizeClause(string clause)
    {
        ArgumentNullException.ThrowIfNull(clause);

        string text = clause.Trim().TrimEnd('.');
        if (text.StartsWith("Annex ", StringComparison.OrdinalIgnoreCase))
        {
            text = text["Annex ".Length..].Trim();
        }

        return text;
    }

    private static bool IsCited(string typeName, DocumentationIndex documentation, Regex[] forms)
    {
        // A nested type is covered by the citation of a type that encloses it, as members are by their declaring type's.
        string? name = typeName;
        while (name is not null)
        {
            string? text = documentation.TypeText(name);
            if (text is not null && forms.Any(form => form.IsMatch(text)))
            {
                return true;
            }

            int dot = name.LastIndexOf('.');
            name = dot < 0 ? null : name[..dot];
            if (name is not null && documentation.TypeText(name) is null)
            {
                // The enclosing name is a namespace, not a type.
                break;
            }
        }

        return false;
    }

    private static int LineOf(string[] lines, string search)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(search, StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        return 1;
    }

    [GeneratedRegex("`([^`]*)`")]
    private static partial Regex Backticked();

    // "§7.3.4", "Annex D", "clause 9", the key of an unnumbered clause ("§BPC-3", "§Explicit"), or a bare clause right after the
    // document's name ("ITU-T T.800 I.5.3", "Application Note 001 BPC-3").
    [GeneratedRegex(@"(?:(?:§\s*|Annex\s+|[Cc]lause\s+)(?<clause>[A-Z]{2,4}-\d+|[A-Z][a-z]+|[A-Z](?:\.\d+)*|\d+(?:\.\d+)*)|^[\s,:]*(?<clause>[A-Z]{1,4}-\d+|[A-Z](?:\.\d+)+|\d+(?:\.\d+)+))(?![\w-])")]
    private static partial Regex ClauseReference();
}

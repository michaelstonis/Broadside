using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Broadside.Tools.ConformanceCheck;

/// <summary>The allowed values of the Status cell of a conformance row.</summary>
public static class ConformanceStatus
{
    /// <summary>Nothing of the clause is implemented.</summary>
    public const string NotStarted = "not started";

    /// <summary>Some of the clause is implemented; the Notes cell says what is missing.</summary>
    public const string Partial = "partial";

    /// <summary>The clause is implemented and covered by the tests named in the Tests cell.</summary>
    public const string Done = "done";

    /// <summary>The clause has no implementable content or is out of scope; the Notes cell says why.</summary>
    public const string NotApplicable = "n/a";

    /// <summary>Every allowed status, in the order the summary table lists them.</summary>
    public static IReadOnlyList<string> All { get; } = [NotStarted, Partial, Done, NotApplicable];

    /// <summary>Whether <paramref name="status"/> is one of <see cref="All"/>.</summary>
    public static bool IsValid(string status) => All.Contains(status, StringComparer.Ordinal);
}

/// <summary>One row of a conformance table: one clause of one specification.</summary>
/// <param name="File">Repository-relative path of the file with forward slashes, e.g. <c>docs/conformance/iso-32000-2.md</c>.</param>
/// <param name="Line">1-based line number of the row.</param>
/// <param name="Clause">The Clause cell, trimmed.</param>
/// <param name="Title">The Title cell, trimmed.</param>
/// <param name="Status">The Status cell, trimmed; see <see cref="ConformanceStatus"/>.</param>
/// <param name="ImplementingTypes">The Implementing types cell, trimmed and unparsed.</param>
/// <param name="Tests">The Tests cell, trimmed and unparsed.</param>
/// <param name="Notes">The Notes cell, trimmed.</param>
public sealed record ConformanceRow(string File, int Line, string Clause, string Title, string Status, string ImplementingTypes, string Tests, string Notes);

/// <summary>A parsed conformance file.</summary>
/// <param name="File">Repository-relative path with forward slashes.</param>
/// <param name="Rows">The data rows in file order. Rows with the wrong cell count are reported as violations and omitted.</param>
public sealed record ConformanceFile(string File, IReadOnlyList<ConformanceRow> Rows)
{
    /// <summary>The number of rows whose Status cell equals <paramref name="status"/> exactly.</summary>
    public int Count(string status) => Rows.Count(row => string.Equals(row.Status, status, StringComparison.Ordinal));
}

/// <summary>One rule failure. <see cref="ToString"/> prints it as <c>docs/conformance/&lt;file&gt;.md:&lt;line&gt;: &lt;message&gt;</c>.</summary>
/// <param name="File">Repository-relative path with forward slashes.</param>
/// <param name="Line">1-based line number of the offending row.</param>
/// <param name="Message">What is wrong.</param>
public sealed record Violation(string File, int Line, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{File}:{Line}: {Message}");
}

/// <summary>Parses the Markdown tables of a conformance file into <see cref="ConformanceRow"/>s.</summary>
public static partial class ConformanceMapParser
{
    /// <summary>The header cells every conformance table must have, in order.</summary>
    public static IReadOnlyList<string> ExpectedHeaders { get; } = ["Clause", "Title", "Status", "Implementing types", "Tests", "Notes"];

    private static readonly string ExpectedHeaderList = string.Join(", ", ExpectedHeaders);

    /// <summary>Parses <paramref name="lines"/>, adding structural problems (wrong header, wrong cell count) to <paramref name="violations"/>.</summary>
    /// <param name="file">Repository-relative path with forward slashes, used in rows and violations.</param>
    /// <param name="lines">The file's lines.</param>
    /// <param name="violations">Receives one entry per malformed line.</param>
    public static ConformanceFile Parse(string file, IReadOnlyList<string> lines, ICollection<Violation> violations)
    {
        var rows = new List<ConformanceRow>();
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i].Trim();
            if (!line.StartsWith('|'))
            {
                continue;
            }

            int lineNumber = i + 1;
            List<string> cells = SplitCells(line);
            if (IsSeparator(cells))
            {
                continue;
            }

            bool isHeader = i + 1 < lines.Count && IsSeparator(SplitCells(lines[i + 1].Trim()));
            if (isHeader)
            {
                if (!cells.SequenceEqual(ExpectedHeaders, StringComparer.Ordinal))
                {
                    violations.Add(new Violation(file, lineNumber, $"expected header cells {ExpectedHeaderList} but found {string.Join(", ", cells)}"));
                }

                continue;
            }

            if (cells.Count != ExpectedHeaders.Count)
            {
                violations.Add(new Violation(file, lineNumber, string.Create(CultureInfo.InvariantCulture, $"expected {ExpectedHeaders.Count} cells ({ExpectedHeaderList}) but found {cells.Count}")));
                continue;
            }

            rows.Add(new ConformanceRow(file, lineNumber, cells[0], cells[1], cells[2], cells[3], cells[4], cells[5]));
        }

        return new ConformanceFile(file, rows);
    }

    /// <summary>Splits a table line into trimmed cells, dropping the leading and trailing pipe. <c>\|</c> is an escaped pipe.</summary>
    private static List<string> SplitCells(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        int start = line.StartsWith('|') ? 1 : 0;
        for (int i = start; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\\' && i + 1 < line.Length && line[i + 1] == '|')
            {
                current.Append('|');
                i++;
            }
            else if (c == '|')
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        // Text after the last pipe is a final cell only when the line does not end with a pipe.
        string trailing = current.ToString().Trim();
        if (trailing.Length > 0)
        {
            cells.Add(trailing);
        }

        return cells;
    }

    private static bool IsSeparator(List<string> cells) => cells.Count > 0 && cells.TrueForAll(static cell => SeparatorCell().IsMatch(cell));

    [GeneratedRegex(@"^:?-+:?$")]
    private static partial Regex SeparatorCell();
}

/// <summary>
/// What the checker knows about the code: test classes and their <c>[Fact]</c>/<c>[Theory]</c> methods under <c>tests/</c>, and the
/// simple names of every <c>class</c>, <c>struct</c>, <c>interface</c>, <c>enum</c> or <c>record</c> declared under <c>src/</c>.
/// Built by a line-level regex scan, not by a compiler: it tracks the current <c>namespace</c> and the most recent type declaration
/// in each file, which is enough for the identifier formats the conformance map allows.
/// </summary>
public sealed partial class CodeIndex
{
    private static readonly string[] SkippedDirectories = ["bin", "obj", "Fixtures"];

    private readonly Dictionary<string, HashSet<string>> _testClasses;
    private readonly HashSet<string> _sourceTypes;

    private CodeIndex(Dictionary<string, HashSet<string>> testClasses, HashSet<string> sourceTypes)
    {
        _testClasses = testClasses;
        _sourceTypes = sourceTypes;
    }

    /// <summary>Scans <c>tests/**/*.cs</c> and <c>src/**/*.cs</c> under <paramref name="repositoryRoot"/>, skipping <c>bin</c>, <c>obj</c> and <c>Fixtures</c> directories.</summary>
    public static CodeIndex Build(string repositoryRoot)
    {
        var testClasses = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (string path in EnumerateSources(Path.Combine(repositoryRoot, "tests")))
        {
            IndexTests(File.ReadLines(path), testClasses);
        }

        var sourceTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in EnumerateSources(Path.Combine(repositoryRoot, "src")))
        {
            IndexTypes(File.ReadLines(path), sourceTypes);
        }

        return new CodeIndex(testClasses, sourceTypes);
    }

    /// <summary>Whether a type named <paramref name="fullName"/> (<c>Namespace.Class</c>) is declared under <c>tests/</c>.</summary>
    public bool HasTestClass(string fullName) => _testClasses.ContainsKey(fullName);

    /// <summary>Whether <paramref name="className"/> (<c>Namespace.Class</c>) declares a <c>[Fact]</c> or <c>[Theory]</c> method named <paramref name="methodName"/>.</summary>
    public bool HasTestMethod(string className, string methodName) => _testClasses.TryGetValue(className, out HashSet<string>? methods) && methods.Contains(methodName);

    /// <summary>Whether a type with the simple name <paramref name="simpleName"/> is declared under <c>src/</c>.</summary>
    public bool HasSourceType(string simpleName) => _sourceTypes.Contains(simpleName);

    private static IEnumerable<string> EnumerateSources(string directory)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            foreach (string file in Directory.EnumerateFiles(current, "*.cs"))
            {
                yield return file;
            }

            foreach (string child in Directory.EnumerateDirectories(current))
            {
                if (!SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.Ordinal))
                {
                    pending.Push(child);
                }
            }
        }
    }

    private static void IndexTests(IEnumerable<string> lines, Dictionary<string, HashSet<string>> testClasses)
    {
        string currentNamespace = string.Empty;
        HashSet<string>? currentClass = null;
        bool pendingTestAttribute = false;
        foreach (string rawLine in lines)
        {
            string line = StripComments(rawLine);
            if (line.Length == 0)
            {
                continue;
            }

            Match ns = NamespaceDeclaration().Match(line);
            if (ns.Success)
            {
                currentNamespace = ns.Groups[1].Value;
                continue;
            }

            Match type = TypeDeclaration().Match(line);
            if (type.Success)
            {
                string fullName = currentNamespace.Length == 0 ? type.Groups[1].Value : currentNamespace + "." + type.Groups[1].Value;
                if (!testClasses.TryGetValue(fullName, out currentClass))
                {
                    currentClass = new HashSet<string>(StringComparer.Ordinal);
                    testClasses.Add(fullName, currentClass);
                }

                pendingTestAttribute = false;
                continue;
            }

            if (TestAttribute().IsMatch(line))
            {
                pendingTestAttribute = true;
            }

            if (!pendingTestAttribute)
            {
                continue;
            }

            string declaration = LeadingAttributes().Replace(line, string.Empty).Trim();
            if (declaration.Length == 0)
            {
                continue;
            }

            Match method = MethodDeclaration().Match(declaration);
            if (method.Success)
            {
                currentClass?.Add(method.Groups[1].Value);
            }

            pendingTestAttribute = false;
        }
    }

    private static void IndexTypes(IEnumerable<string> lines, HashSet<string> types)
    {
        foreach (string rawLine in lines)
        {
            string line = StripComments(rawLine);
            foreach (Match match in TypeDeclaration().Matches(line))
            {
                types.Add(match.Groups[1].Value);
            }
        }
    }

    private static string StripComments(string line)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*'))
        {
            return string.Empty;
        }

        int comment = trimmed.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? trimmed : trimmed[..comment];
    }

    [GeneratedRegex(@"^namespace\s+([A-Za-z_][\w.]*)")]
    private static partial Regex NamespaceDeclaration();

    [GeneratedRegex(@"(?<![\w.])(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+([A-Za-z_]\w*)")]
    private static partial Regex TypeDeclaration();

    [GeneratedRegex(@"\[\s*(?:[A-Za-z_]\w*\.)*\w*(?:Fact|Theory)(?:Attribute)?\s*[\](,]")]
    private static partial Regex TestAttribute();

    [GeneratedRegex(@"^\s*(?:\[[^\]]*\]\s*)+")]
    private static partial Regex LeadingAttributes();

    [GeneratedRegex(@"([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(")]
    private static partial Regex MethodDeclaration();
}

/// <summary>The outcome of checking a repository's conformance map.</summary>
/// <param name="Files">Every conformance file, sorted by name.</param>
/// <param name="Violations">Every rule failure, in file order then line order.</param>
public sealed record ConformanceReport(IReadOnlyList<ConformanceFile> Files, IReadOnlyList<Violation> Violations)
{
    /// <summary>Whether no rule failed.</summary>
    public bool IsValid => Violations.Count == 0;

    /// <summary>The number of rows across every file.</summary>
    public int RowCount => Files.Sum(static file => file.Rows.Count);

    /// <summary>A Markdown table with one row per file, one column per status, and a total row.</summary>
    public string ToMarkdownSummary()
    {
        var text = new StringBuilder();
        text.Append("| Specification |");
        foreach (string status in ConformanceStatus.All)
        {
            text.Append(' ').Append(status).Append(" |");
        }

        text.AppendLine(" Rows |");
        text.Append("|---|");
        foreach (string _ in ConformanceStatus.All)
        {
            text.Append("---:|");
        }

        text.AppendLine("---:|");
        foreach (ConformanceFile file in Files)
        {
            text.Append("| ").Append(Path.GetFileName(file.File)).Append(" |");
            foreach (string status in ConformanceStatus.All)
            {
                text.Append(' ').Append(file.Count(status).ToString(CultureInfo.InvariantCulture)).Append(" |");
            }

            text.Append(' ').Append(file.Rows.Count.ToString(CultureInfo.InvariantCulture)).AppendLine(" |");
        }

        text.Append("| **Total** |");
        foreach (string status in ConformanceStatus.All)
        {
            text.Append(' ').Append(Files.Sum(file => file.Count(status)).ToString(CultureInfo.InvariantCulture)).Append(" |");
        }

        text.Append(' ').Append(RowCount.ToString(CultureInfo.InvariantCulture)).Append(" |");
        return text.ToString();
    }
}

/// <summary>Applies the rules in <c>docs/conformance/README.md</c> to every <c>docs/conformance/*.md</c> except <c>README.md</c>.</summary>
public static partial class ConformanceChecker
{
    private const string ConformanceDirectory = "docs/conformance";

    /// <summary>Checks the conformance map under <paramref name="repositoryRoot"/>.</summary>
    /// <param name="repositoryRoot">The directory that holds <c>docs/</c>, <c>src/</c> and <c>tests/</c>.</param>
    /// <param name="strict">Also fail <c>partial</c> rows with an empty Tests or Notes cell.</param>
    /// <exception cref="DirectoryNotFoundException"><paramref name="repositoryRoot"/> has no <c>docs/conformance</c> directory.</exception>
    public static ConformanceReport Check(string repositoryRoot, bool strict)
    {
        string directory = Path.Combine(repositoryRoot, "docs", "conformance");
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"No {ConformanceDirectory} directory under '{repositoryRoot}'.");
        }

        string[] paths = Directory.GetFiles(directory, "*.md");
        Array.Sort(paths, StringComparer.Ordinal);

        CodeIndex index = CodeIndex.Build(repositoryRoot);
        var files = new List<ConformanceFile>();
        var violations = new List<Violation>();
        foreach (string path in paths)
        {
            string name = Path.GetFileName(path);
            if (string.Equals(name, "README.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileViolations = new List<Violation>();
            ConformanceFile file = ConformanceMapParser.Parse(ConformanceDirectory + "/" + name, File.ReadAllLines(path), fileViolations);
            Validate(file, index, strict, fileViolations);
            fileViolations.Sort(static (a, b) => a.Line.CompareTo(b.Line));
            files.Add(file);
            violations.AddRange(fileViolations);
        }

        return new ConformanceReport(files, violations);
    }

    /// <summary>Applies the row rules to one parsed file.</summary>
    /// <param name="file">The parsed file.</param>
    /// <param name="index">The code index the Tests and Implementing types cells are resolved against.</param>
    /// <param name="strict">Also fail <c>partial</c> rows with an empty Tests or Notes cell.</param>
    /// <param name="violations">Receives one entry per failed rule.</param>
    public static void Validate(ConformanceFile file, CodeIndex index, bool strict, ICollection<Violation> violations)
    {
        var clauses = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ConformanceRow row in file.Rows)
        {
            void Report(string message) => violations.Add(new Violation(row.File, row.Line, message));

            if (row.Clause.Length == 0)
            {
                Report("Clause cell is empty");
            }
            else if (clauses.TryGetValue(row.Clause, out int firstLine))
            {
                Report(string.Create(CultureInfo.InvariantCulture, $"duplicate clause '{row.Clause}' (first at line {firstLine})"));
            }
            else
            {
                clauses.Add(row.Clause, row.Line);
            }

            if (!ConformanceStatus.IsValid(row.Status))
            {
                Report($"status '{row.Status}' is not one of: {string.Join(", ", ConformanceStatus.All)}");
            }

            switch (row.Status)
            {
                case ConformanceStatus.Done when row.Tests.Length == 0:
                    Report("done row must name at least one test in the Tests cell");
                    break;
                case ConformanceStatus.Partial when strict:
                    if (row.Tests.Length == 0)
                    {
                        Report("partial row must name at least one test in the Tests cell (--strict)");
                    }

                    if (row.Notes.Length == 0)
                    {
                        Report("partial row must say what is missing in the Notes cell (--strict)");
                    }

                    break;
                case ConformanceStatus.NotApplicable when row.Notes.Length == 0:
                    Report("n/a row must say why in the Notes cell");
                    break;
                default:
                    break;
            }

            if (row.Tests.Length > 0)
            {
                if (TryParseIdentifierList(row.Tests, out List<string> tests))
                {
                    foreach (string test in tests)
                    {
                        string? problem = ResolveTest(test, index);
                        if (problem is not null)
                        {
                            Report(problem);
                        }
                    }
                }
                else
                {
                    Report("Tests cell must be comma-separated backticked identifiers like `Namespace.Class.Method` or `Namespace.Class`");
                }
            }

            if (row.ImplementingTypes.Length > 0)
            {
                if (TryParseIdentifierList(row.ImplementingTypes, out List<string> types))
                {
                    foreach (string type in types)
                    {
                        if (!SimpleTypeName().IsMatch(type))
                        {
                            Report($"implementing type '{type}' is not a simple type name (no namespace or type arguments)");
                        }
                        else if (!index.HasSourceType(type))
                        {
                            Report($"implementing type '{type}' is not declared as a class, struct, interface, enum or record in src/");
                        }
                    }
                }
                else
                {
                    Report("Implementing types cell must be comma-separated backticked type names like `CosDictionary`");
                }
            }
        }
    }

    /// <summary>Resolves a Tests-cell identifier against the index; returns the violation message, or <see langword="null"/> when it resolves.</summary>
    private static string? ResolveTest(string identifier, CodeIndex index)
    {
        if (!QualifiedName().IsMatch(identifier))
        {
            return $"test '{identifier}' is not a Namespace.Class or Namespace.Class.Method identifier";
        }

        if (index.HasTestClass(identifier))
        {
            return null;
        }

        int lastDot = identifier.LastIndexOf('.');
        string className = identifier[..lastDot];
        string methodName = identifier[(lastDot + 1)..];
        return index.HasTestMethod(className, methodName)
            ? null
            : $"test '{identifier}' was not found in tests/: no class '{identifier}' and no [Fact]/[Theory] method '{methodName}' on class '{className}'";
    }

    /// <summary>Parses a cell of comma-separated backticked identifiers. Fails on text outside the backticks and on empty backticks.</summary>
    private static bool TryParseIdentifierList(string cell, out List<string> identifiers)
    {
        identifiers = [];
        MatchCollection matches = Backticked().Matches(cell);
        if (matches.Count == 0)
        {
            return false;
        }

        foreach (Match match in matches)
        {
            string identifier = match.Groups[1].Value.Trim();
            if (identifier.Length == 0)
            {
                return false;
            }

            identifiers.Add(identifier);
        }

        string remainder = Backticked().Replace(cell, string.Empty);
        return remainder.All(static c => c == ',' || char.IsWhiteSpace(c));
    }

    [GeneratedRegex("`([^`]*)`")]
    private static partial Regex Backticked();

    [GeneratedRegex(@"^[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)+$")]
    private static partial Regex QualifiedName();

    [GeneratedRegex(@"^[A-Za-z_]\w*$")]
    private static partial Regex SimpleTypeName();
}

/// <summary>Finds the repository root: the nearest ancestor directory that contains <c>Broadside.slnx</c>.</summary>
public static class RepositoryLocator
{
    /// <summary>The file that marks the repository root.</summary>
    public const string Marker = "Broadside.slnx";

    /// <summary>Walks up from <paramref name="start"/>; returns the directory that holds <see cref="Marker"/>, or <see langword="null"/>.</summary>
    public static string? FindRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, Marker)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}

namespace Broadside.ApiGuard.Tests;

/// <summary>
/// One entry of a <c>PublicAPI.*.txt</c> file, split into what the rules read: <c>static Broadside.X.Y.OpenAsync(string! path,
/// System.Threading.CancellationToken cancellationToken = default(...)) -&gt; System.Threading.Tasks.Task!</c> is the member
/// <c>Broadside.X.Y.OpenAsync</c>, two parameters and a result.
/// </summary>
internal sealed record PublicApiLine(string Package, string Text, bool Oblivious, string Modifiers, string Member, IReadOnlyList<string>? Parameters, string? Result)
{
    /// <summary>The member's simple name (<c>OpenAsync</c>; <c>get</c> for a getter, the type name for a type).</summary>
    public string Name => Member[(Member.LastIndexOf('.') + 1)..];

    /// <summary>The containing type or namespace (<c>Broadside.X.Y</c>).</summary>
    public string Container => Member.LastIndexOf('.') is var dot and > 0 ? Member[..dot] : "";

    /// <summary>A type declaration: a bare name, no parameters and no result.</summary>
    public bool IsType => Parameters is null && Result is null;

    private static readonly string[] ModifierWords = ["abstract", "static", "virtual", "override", "const", "readonly", "sealed", "new"];

    /// <summary>Parses one line; <see langword="null"/> for blank lines, directives and removals.</summary>
    public static PublicApiLine? Parse(string package, string line)
    {
        string text = line.Trim();
        if (text.Length == 0 || text.StartsWith('#') || text.StartsWith("*REMOVED*", StringComparison.Ordinal))
        {
            return null;
        }

        bool oblivious = text.StartsWith('~');
        string rest = oblivious ? text[1..] : text;
        List<string> modifiers = [];
        while (rest.IndexOf(' ', StringComparison.Ordinal) is var space and > 0 && ModifierWords.Contains(rest[..space]))
        {
            modifiers.Add(rest[..space]);
            rest = rest[(space + 1)..];
        }

        int arrow = TopLevelArrow(rest);
        string signature = arrow >= 0 ? rest[..arrow] : rest;
        string? result = arrow >= 0 ? rest[(arrow + 4)..] : null;
        int open = signature.IndexOf('(', StringComparison.Ordinal);
        int equals = signature.IndexOf(" = ", StringComparison.Ordinal);
        if (open < 0 || (equals >= 0 && equals < open))
        {
            // A type, a property accessor, a field or a constant (whose value follows " = ").
            string member = equals >= 0 ? signature[..equals] : signature;
            return new PublicApiLine(package, text, oblivious, string.Join(' ', modifiers), member.Trim(), null, result);
        }

        string name = signature[..open];
        int generic = name.IndexOf('<', StringComparison.Ordinal);
        if (generic >= 0)
        {
            name = name[..generic];
        }

        int close = signature.LastIndexOf(')');
        return new PublicApiLine(package, text, oblivious, string.Join(' ', modifiers), name, SplitTopLevel(signature[(open + 1)..close]), result);
    }

    private static int TopLevelArrow(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length - 3; i++)
        {
            depth += text[i] switch { '(' or '<' => 1, ')' or '>' when !(text[i] == '>' && i > 0 && text[i - 1] == '-') => -1, _ => 0 };
            if (depth == 0 && string.CompareOrdinal(text, i, " -> ", 0, 4) == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<string> SplitTopLevel(string parameters)
    {
        List<string> parts = [];
        int depth = 0;
        int start = 0;
        for (int i = 0; i < parameters.Length; i++)
        {
            switch (parameters[i])
            {
                case '(' or '<' or '[':
                    depth++;
                    break;
                case ')' or '>' or ']':
                    depth--;
                    break;
                case ',' when depth == 0:
                    parts.Add(parameters[start..i].Trim());
                    start = i + 1;
                    break;
            }
        }

        if (parameters.Trim().Length > 0)
        {
            parts.Add(parameters[start..].Trim());
        }

        return parts;
    }
}

/// <summary>
/// The rules a <c>PublicAPI.*.txt</c> line can show, for packages that cannot be loaded here (the platform backends) and for
/// nullability, which the files record more directly than metadata: an oblivious member is a line starting with <c>~</c>.
/// </summary>
internal static class PublicApiFiles
{
    private const string CancellationToken = "System.Threading.CancellationToken";

    /// <summary>The <c>PublicAPI.Shipped.txt</c> and <c>PublicAPI.Unshipped.txt</c> paths of a package under <c>src/</c>.</summary>
    public static IEnumerable<string> Paths(string package) =>
        new[] { "PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt" }.Select(file => Path.Combine(ShippingPackages.RepositoryRoot, "src", package, file));

    /// <summary>A package's parsed entries.</summary>
    public static IEnumerable<PublicApiLine> Read(string package) =>
        Paths(package).Where(File.Exists).SelectMany(File.ReadLines).Select(line => PublicApiLine.Parse(package, line)).OfType<PublicApiLine>();

    /// <summary>
    /// Nullable annotations: every non-empty file opens with <c>#nullable enable</c>, and no member is oblivious (a <c>~</c> line,
    /// which the analyzer writes for a member compiled without nullable annotations).
    /// </summary>
    public static IEnumerable<Violation> Nullable(string package, IEnumerable<(string File, string[] Lines)> files)
    {
        foreach ((string file, string[] lines) in files)
        {
            string[] content = [.. lines.Where(l => l.Trim().Length > 0)];
            if (content.Length > 0 && content[0].Trim() != "#nullable enable")
            {
                yield return new Violation($"{package}/{Path.GetFileName(file)}", "does not open with #nullable enable");
            }

            foreach (string line in content.Where(l => l.TrimStart().StartsWith('~')))
            {
                yield return new Violation(line.Trim(), "an oblivious member (compiled without nullable annotations)");
            }
        }
    }

    /// <summary>The nullable rule over a package's files on disk.</summary>
    public static IEnumerable<Violation> Nullable(string package) =>
        Nullable(package, Paths(package).Where(File.Exists).Select(path => (path, File.ReadAllLines(path))));

    /// <summary>
    /// The reflection rules a signature line can show: namespaces and prefixes, the cancellation token last, <c>Async</c> naming,
    /// optional parameters on overloads, mutable collections, glossary words. Sealing, extension points and Use*/With* need the
    /// assembly and are reviewed by hand for these packages (their surfaces are a few types each).
    /// </summary>
    public static IEnumerable<Violation> Signatures(IEnumerable<PublicApiLine> lines)
    {
        PublicApiLine[] all = [.. lines];
        foreach (PublicApiLine line in all.Where(l => l.IsType))
        {
            string ns = line.Container;
            if (!ApiRules.IsAllowedNamespace(ns))
            {
                yield return new Violation(line.Member, $"namespace '{ns}' is not the root or one of the spec's areas");
            }

            PublicApiLine[] members = [.. all.Where(l => l.Container == line.Member)];
            bool extensionClass = ApiRules.IsExtensionClassName(line.Name, isStatic: members.Length > 0 && members.All(l => l.Modifiers.Contains("static", StringComparison.Ordinal)));
            if (ApiRules.IsUnprefixedArea(ns) && (ApiRules.HasPrefix(line.Name, "Pdf") || ApiRules.HasPrefix(line.Name, "Cos")) && !extensionClass)
            {
                yield return new Violation(line.Member, "Rendering, Text and Layout types carry no Pdf or Cos prefix");
            }
        }

        foreach (PublicApiLine line in all)
        {
            bool constructor = line.Parameters is not null && line.Container.EndsWith("." + line.Name, StringComparison.Ordinal);
            string? glossary = line.IsType ? ApiRules.AvoidedInTypeName(line.Name)
                : line.Parameters is not null && !constructor ? ApiRules.AvoidedInMethodName(line.Name) : null;
            if (glossary is not null)
            {
                yield return new Violation(line.IsType ? line.Member : line.Text, glossary);
            }

            if (line.Result is { } result && (result.Contains("[]", StringComparison.Ordinal) && line.Name == "get" || MutableCollection(result) is not null))
            {
                yield return new Violation(line.Text, "exposes a mutable collection or an array property");
            }

            if (line.Parameters is not { } parameters)
            {
                continue;
            }

            int tokens = parameters.Count(p => p.StartsWith(CancellationToken + " ", StringComparison.Ordinal));
            if (tokens > 0 && (tokens > 1 || !parameters[^1].StartsWith(CancellationToken + " cancellationToken", StringComparison.Ordinal)))
            {
                yield return new Violation(line.Text, "the cancellation token is not the last parameter named cancellationToken");
            }

            bool awaitable = line.Result is { } r && (r.StartsWith("System.Threading.Tasks.Task", StringComparison.Ordinal)
                || r.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal) || r.StartsWith("System.Collections.Generic.IAsyncEnumerable<", StringComparison.Ordinal));
            bool named = line.Name.EndsWith("Async", StringComparison.Ordinal);
            if (line.Name != "DisposeAsync" && !line.Modifiers.Contains("override", StringComparison.Ordinal) && (named != awaitable || (awaitable && tokens == 0)))
            {
                yield return new Violation(line.Text, named != awaitable ? "*Async naming does not match an awaitable result" : "an asynchronous method without a cancellation token");
            }
        }

        foreach (IGrouping<string, PublicApiLine> overloads in all.Where(l => l.Parameters is not null).GroupBy(l => l.Member))
        {
            if (overloads.Count() < 2)
            {
                continue;
            }

            foreach (PublicApiLine line in overloads)
            {
                IReadOnlyList<string> parameters = line.Parameters!;
                if (parameters.Where((p, i) => p.Contains(" = ", StringComparison.Ordinal) && !(i == parameters.Count - 1 && p.StartsWith(CancellationToken, StringComparison.Ordinal))).Any())
                {
                    yield return new Violation(line.Text, "an overloaded method with optional parameters");
                }
            }
        }
    }

    private static string? MutableCollection(string type) =>
        new[]
        {
            "System.Collections.Generic.List<", "System.Collections.Generic.Dictionary<", "System.Collections.Generic.HashSet<",
            "System.Collections.Generic.IList<", "System.Collections.Generic.ICollection<", "System.Collections.Generic.IDictionary<",
            "System.Collections.Generic.ISet<", "System.Collections.ObjectModel.Collection<",
        }.FirstOrDefault(m => type.Contains(m, StringComparison.Ordinal));
}

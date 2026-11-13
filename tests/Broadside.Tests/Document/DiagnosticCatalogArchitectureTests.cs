using System.Reflection;
using System.Text.RegularExpressions;
using Broadside.Diagnostics;

namespace Broadside.Tests.Document;

/// <summary>
/// The public diagnostic catalog is the only home of a diagnostic code (issue #247, ADR 0005): a code is declared once, in the
/// <c>*Diagnostics</c> class of its area in <c>src/Broadside/Diagnostics</c>, and everything else refers to it. Nothing in the
/// solution declares a <see cref="DiagnosticCode"/> member, or a string constant that is a code's name, anywhere else.
/// </summary>
public sealed partial class DiagnosticCatalogArchitectureTests
{
    private static readonly string[] Roots = ["src", "bench", "tools", "samples", "tests"];

    private static readonly string[] SkippedFolders = ["obj", "bin", "node_modules", ".git"];

    /// <summary>A static member of type <see cref="DiagnosticCode"/> (property or field; a method returning one is not a declaration).</summary>
    [GeneratedRegex(@"\bstatic\s+(?:readonly\s+)?DiagnosticCode\??\s+\w+\s*(?:\{|=|;)")]
    private static partial Regex TypedCodeMember();

    /// <summary>A string constant whose value is its own name: the shape every code had before the catalog, and what a hand-made one would copy.</summary>
    [GeneratedRegex(@"\bconst\s+string\s+(\w+)\s*=\s*(?:""\1""|nameof\(\1\))\s*;")]
    private static partial Regex NameConstant();

    /// <summary>A string member that forwards to a catalog code's name under the same name: the old forwarding shape.</summary>
    [GeneratedRegex(@"\bstring\s+(\w+)\s*=>\s*\w+Diagnostics\.\1\.Name\b")]
    private static partial Regex ForwardedName();

    [Fact]
    public void A_diagnostic_code_is_declared_only_in_the_public_catalog_classes()
    {
        Assert.Empty(Offenders(RepositoryRoot()));
    }

    [Fact]
    public void The_guard_catches_a_code_declared_outside_the_catalog()
    {
        // The fixture text is assembled from pieces so that this file does not itself contain a declaration the guard would flag.
        string root = Directory.CreateTempSubdirectory("broadside-catalog-guard").FullName;
        try
        {
            Write(root, "src/Broadside/Diagnostics/FakeDiagnostics.cs", "public static class FakeDiagnostics { public static " + "DiagnosticCode Fine { get; } = null!; }");
            Write(root, "src/Broadside/Parsing/Late.cs", "internal static class Late { public static " + "DiagnosticCode LateCode { get; } = null!; }");
            Write(root, "src/Broadside.Layout/Constants.cs", "internal static class Constants { public " + "const string LateName = \"LateName\"; }");
            Write(root, "src/Broadside.Layout/Named.cs", "internal static class Named { public " + "const string Other = nameof(Other); }");
            Write(root, "tests/Some.Tests/Forward.cs", "internal static class Forward { public static " + "string Forwarded => FakeDiagnostics.Forwarded.Name; }");
            Write(root, "src/Broadside/Parsing/Fine.cs", "internal static class Fine { internal static " + "DiagnosticCode Pick(int i) => FakeDiagnostics.Fine; public const " + "string Module = \"module\"; }");
            Write(root, "tests/Some.Tests/Mine.cs", "internal static class Mine { public static " + "DiagnosticCode Tests { get; } = DiagnosticCode.Create(\"Tests\", \"Custom\", DiagnosticSeverity.Information); }");
            Write(root, "src/Broadside/obj/Generated.cs", "internal static class G { public static " + "DiagnosticCode Ignored { get; } = null!; }");

            string[] files = [.. Offenders(root).Select(offender => offender[..offender.IndexOf(':', StringComparison.Ordinal)]).Order(StringComparer.Ordinal)];

            Assert.Equal(
                ["src/Broadside.Layout/Constants.cs", "src/Broadside.Layout/Named.cs", "src/Broadside/Parsing/Late.cs", "tests/Some.Tests/Forward.cs"],
                files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_catalog_class_declares_nothing_but_codes_and_its_list()
    {
        Type[] catalogClasses = [.. typeof(DiagnosticCode).Assembly.GetTypes()
            .Where(type => type.Namespace == "Broadside.Diagnostics" && type.Name.EndsWith("Diagnostics", StringComparison.Ordinal) && type.IsAbstract && type.IsSealed)];
        Assert.NotEmpty(catalogClasses);

        foreach (Type type in catalogClasses)
        {
            Assert.True(type.IsPublic, type.Name);
            Assert.True(File.Exists(Path.Combine(RepositoryRoot(), "src", "Broadside", "Diagnostics", type.Name + ".cs")), type.Name);
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                bool isCode = property.PropertyType == typeof(DiagnosticCode);
                bool isList = property.Name == "Codes" && property.PropertyType == typeof(DiagnosticCode[]);
                Assert.True(isCode || isList, $"{type.Name}.{property.Name} is not a code");
            }

            Assert.All(
                type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly),
                field => Assert.StartsWith("<", field.Name, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Every_filter_and_image_code_is_reported_somewhere()
    {
        Type[] catalogClasses = [typeof(FilterDiagnostics), typeof(DctDiagnostics), typeof(CcittDiagnostics), typeof(Jbig2Diagnostics), typeof(JpxDiagnostics), typeof(ImageDiagnostics)];
        string source = Path.Combine(RepositoryRoot(), "src");
        string[] sources = [.. Files(source)
            .Where(file => !IsCatalogClass(Path.GetRelativePath(RepositoryRoot(), file).Replace('\\', '/')))
            .Select(File.ReadAllText)];

        var neverReported = new List<string>();
        foreach (Type catalogClass in catalogClasses)
        {
            foreach (PropertyInfo property in catalogClass.GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(DiagnosticCode)))
            {
                var use = new Regex(@"\b" + Regex.Escape(catalogClass.Name + "." + property.Name) + @"\b");
                if (!sources.Any(text => use.IsMatch(text)))
                {
                    neverReported.Add(catalogClass.Name + "." + property.Name);
                }
            }
        }

        Assert.Empty(neverReported);
    }

    /// <summary>The offenders under <paramref name="root"/>, each as <c>relative/path.cs: declaration</c>.</summary>
    private static string[] Offenders(string root)
    {
        Regex[] patterns = [TypedCodeMember(), NameConstant(), ForwardedName()];
        var offenders = new List<string>();
        foreach (string folder in Roots)
        {
            string start = Path.Combine(root, folder);
            if (!Directory.Exists(start))
            {
                continue;
            }

            foreach (string file in Files(start))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (IsCatalogClass(relative))
                {
                    continue;
                }

                string text = File.ReadAllText(file);
                foreach (Regex pattern in patterns)
                {
                    foreach (Match match in pattern.Matches(text))
                    {
                        if (IsThirdPartyCode(text, match))
                        {
                            continue;
                        }

                        offenders.Add($"{relative}: {match.Value}");
                    }
                }
            }
        }

        return [.. offenders];
    }

    /// <summary>A member initialized by <c>DiagnosticCode.Create</c> is an owner-prefixed third-party code (a test fixture, say), not a Broadside code.</summary>
    private static bool IsThirdPartyCode(string text, Match match)
    {
        int end = text.IndexOf(';', match.Index);
        return end > 0 && text.AsSpan(match.Index, end - match.Index).Contains("DiagnosticCode.Create(", StringComparison.Ordinal);
    }

    private static bool IsCatalogClass(string relative) =>
        relative.StartsWith("src/Broadside/Diagnostics/", StringComparison.Ordinal) && relative.EndsWith("Diagnostics.cs", StringComparison.Ordinal);

    private static IEnumerable<string> Files(string folder)
    {
        foreach (string file in Directory.EnumerateFiles(folder, "*.cs"))
        {
            yield return file;
        }

        foreach (string child in Directory.EnumerateDirectories(folder))
        {
            if (!SkippedFolders.Contains(Path.GetFileName(child), StringComparer.Ordinal))
            {
                foreach (string file in Files(child))
                {
                    yield return file;
                }
            }
        }
    }

    private static void Write(string root, string relative, string text)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find Broadside.slnx above the test output directory.");
    }
}

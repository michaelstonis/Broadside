using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Broadside.Tools.PackageCheck;

/// <summary>One problem with one package.</summary>
/// <param name="Package">The package file name.</param>
/// <param name="Message">What is wrong.</param>
public sealed record Finding(string Package, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Package}: {Message}";
}

/// <summary>The parts of a nuspec the checks read.</summary>
/// <param name="Id">Package ID.</param>
/// <param name="Version">Package version.</param>
/// <param name="LicenseType"><c>expression</c> or <c>file</c>, or null when there is no <c>license</c> element.</param>
/// <param name="License">The licence expression or file path.</param>
/// <param name="Readme">The <c>readme</c> path, or null.</param>
/// <param name="Icon">The <c>icon</c> path, or null.</param>
/// <param name="RepositoryUrl">The <c>repository</c> URL, or null.</param>
/// <param name="RepositoryCommit">The <c>repository</c> commit, or null.</param>
/// <param name="Dependencies">Every dependency of every target framework group, as (ID, version range).</param>
public sealed record NuspecInfo(
    string Id,
    string Version,
    string? LicenseType,
    string? License,
    string? Readme,
    string? Icon,
    string? RepositoryUrl,
    string? RepositoryCommit,
    IReadOnlyList<(string Id, string Version)> Dependencies)
{
    /// <summary>Reads the nuspec at the root of a package.</summary>
    /// <exception cref="InvalidDataException">The package has no nuspec, or it lacks an ID or version.</exception>
    public static NuspecInfo Read(ZipArchive package)
    {
        ZipArchiveEntry entry = package.Entries.FirstOrDefault(e => !e.FullName.Contains('/', StringComparison.Ordinal) && e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("the package has no .nuspec at its root");
        XDocument document;
        using (Stream stream = entry.Open())
        {
            document = XDocument.Load(stream);
        }

        XElement metadata = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata")
            ?? throw new InvalidDataException("the nuspec has no metadata element");
        string? Value(string name) => metadata.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
        XElement? license = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "license");
        XElement? repository = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "repository");
        var dependencies = metadata.Descendants()
            .Where(e => e.Name.LocalName == "dependency")
            .Select(e => ((string?)e.Attribute("id") ?? string.Empty, (string?)e.Attribute("version") ?? string.Empty))
            .Distinct()
            .ToList();

        return new NuspecInfo(
            Value("id") ?? throw new InvalidDataException("the nuspec has no id"),
            Value("version") ?? throw new InvalidDataException("the nuspec has no version"),
            (string?)license?.Attribute("type"),
            license?.Value,
            Value("readme"),
            Value("icon"),
            (string?)repository?.Attribute("url"),
            (string?)repository?.Attribute("commit"),
            dependencies);
    }
}

/// <summary>
/// Checks every <c>.nupkg</c> in a folder against a <see cref="PackagePolicy"/>: dependencies on the allowlist, licence metadata and
/// licence files, README, icon and repository metadata in the nuspec, and a <c>.snupkg</c> with a matching portable PDB whose every
/// source document SourceLink can fetch (<see cref="SymbolVerifier"/>).
/// </summary>
public static class PackageVerifier
{
    /// <summary>The package ID prefix of the repository's own packages, which may depend on each other at the same version.</summary>
    public const string OwnPrefix = "Broadside";

    /// <summary>Checks every package in <paramref name="directory"/>; adds every SourceLink URL to <paramref name="sourceUrls"/> when given.</summary>
    /// <returns>The packages checked (file names) and the findings; no findings means every package passed.</returns>
    public static (IReadOnlyList<string> Packages, IReadOnlyList<Finding> Findings) VerifyDirectory(string directory, PackagePolicy policy, ICollection<string>? sourceUrls = null)
    {
        var packages = Directory.GetFiles(directory, "*.nupkg").Order(StringComparer.Ordinal).ToList();
        var findings = new List<Finding>();
        if (packages.Count == 0)
        {
            findings.Add(new Finding(directory, "no .nupkg files"));
        }

        foreach (string package in packages)
        {
            findings.AddRange(VerifyPackage(package, policy, sourceUrls));
        }

        return (packages.Select(Path.GetFileName).ToList()!, findings);
    }

    /// <summary>Checks one package, and its <c>.snupkg</c> beside it; adds every SourceLink URL to <paramref name="sourceUrls"/> when given.</summary>
    public static IReadOnlyList<Finding> VerifyPackage(string packagePath, PackagePolicy policy, ICollection<string>? sourceUrls = null)
    {
        string name = Path.GetFileName(packagePath);
        var findings = new List<Finding>();
        void Fail(string message) => findings.Add(new Finding(name, message));

        using ZipArchive package = ZipFile.OpenRead(packagePath);
        NuspecInfo nuspec;
        try
        {
            nuspec = NuspecInfo.Read(package);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
        {
            Fail(ex.Message);
            return findings;
        }

        var entries = new HashSet<string>(package.Entries.Select(e => e.FullName), StringComparer.OrdinalIgnoreCase);
        PackageRule rule = policy.RuleFor(nuspec.Id);

        foreach ((string id, string range) in nuspec.Dependencies)
        {
            if (IsOwnPackage(id))
            {
                if (!string.Equals(LowerBound(range), nuspec.Version, StringComparison.OrdinalIgnoreCase))
                {
                    Fail($"depends on {id} {range}, not on its own version {nuspec.Version}");
                }
            }
            else if (!rule.Dependencies.Contains(id))
            {
                Fail($"depends on {id} {range}, which is not on its allowlist in the package policy");
            }
        }

        CheckLicense(nuspec, rule, entries, Fail);

        foreach (string file in rule.Files)
        {
            if (!entries.Contains(file))
            {
                Fail($"does not contain {file}");
            }
        }

        if (nuspec.Readme is null)
        {
            Fail("has no readme in its nuspec");
        }
        else if (!entries.Contains(Normalize(nuspec.Readme)))
        {
            Fail($"names readme {nuspec.Readme}, which is not in the package");
        }

        if (nuspec.Icon is null)
        {
            Fail("has no icon in its nuspec");
        }
        else if (!entries.Contains(Normalize(nuspec.Icon)))
        {
            Fail($"names icon {nuspec.Icon}, which is not in the package");
        }

        if (!string.Equals(nuspec.RepositoryUrl, policy.RepositoryUrl, StringComparison.Ordinal))
        {
            Fail($"names repository {nuspec.RepositoryUrl ?? "(none)"}, not {policy.RepositoryUrl}");
        }

        if (nuspec.RepositoryCommit is null || nuspec.RepositoryCommit.Length != 40 || !nuspec.RepositoryCommit.All(char.IsAsciiHexDigitLower))
        {
            Fail($"has no full commit in its repository element ({nuspec.RepositoryCommit ?? "none"})");
        }

        findings.AddRange(SymbolVerifier.Verify(packagePath, package, nuspec, policy, sourceUrls));
        return findings;
    }

    /// <summary>True for this repository's own package IDs (<c>Broadside</c> and <c>Broadside.*</c>).</summary>
    public static bool IsOwnPackage(string id) =>
        string.Equals(id, OwnPrefix, StringComparison.OrdinalIgnoreCase) || id.StartsWith(OwnPrefix + ".", StringComparison.OrdinalIgnoreCase);

    private static void CheckLicense(NuspecInfo nuspec, PackageRule rule, HashSet<string> entries, Action<string> fail)
    {
        const string filePrefix = "file:";
        bool wantsFile = rule.License.StartsWith(filePrefix, StringComparison.Ordinal);
        string expected = wantsFile ? rule.License[filePrefix.Length..] : rule.License;
        string expectedType = wantsFile ? "file" : "expression";
        if (!string.Equals(nuspec.LicenseType, expectedType, StringComparison.Ordinal) || !string.Equals(nuspec.License, expected, StringComparison.Ordinal))
        {
            fail(string.Create(
                CultureInfo.InvariantCulture,
                $"declares licence {nuspec.LicenseType ?? "(none)"} '{nuspec.License}', the policy expects {expectedType} '{expected}'"));
        }

        if (nuspec.LicenseType == "file" && nuspec.License is not null && !entries.Contains(Normalize(nuspec.License)))
        {
            fail($"names licence file {nuspec.License}, which is not in the package");
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    /// <summary>The minimum version of a NuGet version range (<c>1.0</c>, <c>[1.0, )</c>, <c>[1.0]</c>).</summary>
    internal static string LowerBound(string range) => range.Trim().TrimStart('[', '(').Split(',')[0].TrimEnd(']', ')').Trim();
}

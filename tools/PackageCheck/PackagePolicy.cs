using System.Text.Json;
using System.Text.Json.Serialization;

namespace Broadside.Tools.PackageCheck;

/// <summary>The rules one package is held to: allowed dependency IDs, the declared licence and the files that must ship.</summary>
/// <param name="Dependencies">Package IDs the package may depend on, besides other Broadside packages at its own version.</param>
/// <param name="License">An SPDX expression, or <c>file:&lt;path&gt;</c> for a licence file inside the package.</param>
/// <param name="Files">Paths (forward slashes, relative to the package root) that must be in the package.</param>
public sealed record PackageRule(IReadOnlySet<string> Dependencies, string License, IReadOnlyList<string> Files);

/// <summary>
/// <c>package-policy.json</c>: the repository and SourceLink locations every package must point at, a default rule, and per-package
/// overrides. A package with no entry gets the default, so a new package is held to the strictest rule without editing the policy.
/// </summary>
public sealed class PackagePolicy
{
    private readonly Dictionary<string, PackageRule> _rules;

    private PackagePolicy(string repositoryUrl, string sourceLinkUrlPrefix, PackageRule defaultRule, Dictionary<string, PackageRule> rules)
    {
        RepositoryUrl = repositoryUrl;
        SourceLinkUrlPrefix = sourceLinkUrlPrefix;
        Default = defaultRule;
        _rules = rules;
    }

    /// <summary>The URL every nuspec's <c>repository</c> element must name.</summary>
    public string RepositoryUrl { get; }

    /// <summary>Every SourceLink URL must start with this prefix followed by the nuspec's commit and a slash.</summary>
    public string SourceLinkUrlPrefix { get; }

    /// <summary>The rule for packages without an entry.</summary>
    public PackageRule Default { get; }

    /// <summary>The package IDs with an entry of their own.</summary>
    public IEnumerable<string> NamedPackages => _rules.Keys;

    /// <summary>The rule for <paramref name="packageId"/>: its own entry, or the default.</summary>
    public PackageRule RuleFor(string packageId) => _rules.TryGetValue(packageId, out PackageRule? rule) ? rule : Default;

    /// <summary>Loads a policy file.</summary>
    /// <exception cref="InvalidDataException">The file is not a valid policy.</exception>
    public static PackagePolicy Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Parses policy JSON.</summary>
    /// <exception cref="InvalidDataException">The JSON is not a valid policy.</exception>
    public static PackagePolicy Parse(string json)
    {
        PolicyDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(json, PolicyJsonContext.Default.PolicyDocument);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Package policy is not valid JSON: {ex.Message}", ex);
        }

        if (document?.RepositoryUrl is null || document.SourceLinkUrlPrefix is null || document.Default?.Dependencies is null || document.Default.License is null)
        {
            throw new InvalidDataException("Package policy needs repositoryUrl, sourceLinkUrlPrefix and a default with dependencies and license.");
        }

        Dictionary<string, IReadOnlyList<string>> sets = document.DependencySets ?? [];
        PackageRule defaultRule = Resolve(document.Default, null, sets);
        var rules = new Dictionary<string, PackageRule>(StringComparer.OrdinalIgnoreCase);
        foreach ((string id, RuleDocument rule) in document.Packages ?? [])
        {
            rules[id] = Resolve(rule, defaultRule, sets);
        }

        return new PackagePolicy(document.RepositoryUrl, document.SourceLinkUrlPrefix, defaultRule, rules);
    }

    private static PackageRule Resolve(RuleDocument rule, PackageRule? fallback, Dictionary<string, IReadOnlyList<string>> sets)
    {
        IReadOnlySet<string> dependencies = fallback?.Dependencies ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (rule.Dependencies is not null)
        {
            var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in rule.Dependencies)
            {
                if (entry.StartsWith('@'))
                {
                    if (!sets.TryGetValue(entry[1..], out IReadOnlyList<string>? set))
                    {
                        throw new InvalidDataException($"Package policy names an unknown dependency set '{entry}'.");
                    }

                    expanded.UnionWith(set);
                }
                else
                {
                    expanded.Add(entry);
                }
            }

            dependencies = expanded;
        }

        return new PackageRule(dependencies, rule.License ?? fallback?.License ?? string.Empty, rule.Files ?? fallback?.Files ?? []);
    }

    internal sealed class PolicyDocument
    {
        public string? RepositoryUrl { get; set; }

        public string? SourceLinkUrlPrefix { get; set; }

        public Dictionary<string, IReadOnlyList<string>>? DependencySets { get; set; }

        public RuleDocument? Default { get; set; }

        public Dictionary<string, RuleDocument>? Packages { get; set; }
    }

    internal sealed class RuleDocument
    {
        public IReadOnlyList<string>? Dependencies { get; set; }

        public string? License { get; set; }

        public IReadOnlyList<string>? Files { get; set; }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(PackagePolicy.PolicyDocument))]
internal sealed partial class PolicyJsonContext : JsonSerializerContext;

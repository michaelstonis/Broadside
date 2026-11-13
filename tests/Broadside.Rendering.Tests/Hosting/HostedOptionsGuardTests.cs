using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Broadside.Rendering.Tests.Hosting;

/// <summary>
/// Issue #239 (spec #33 user stories 43 to 45): every option reaches the hosted engine. A reflection guard walks every options type
/// in the core and rendering assemblies, so an option a later ticket adds is checked by this class without any change to it.
/// </summary>
public sealed class HostedOptionsGuardTests
{
    private static readonly Assembly[] Assemblies = [typeof(PdfOptions).Assembly, typeof(RenderOptions).Assembly];

    public static TheoryData<string> OptionsTypes => [.. OptionsGuard.Discover(Assemblies).Select(static type => type.FullName!)];

    [Fact]
    public void The_scan_finds_the_engine_save_and_render_options()
    {
        IReadOnlyList<Type> found = OptionsGuard.Discover(Assemblies);

        Assert.Contains(typeof(PdfOptions), found);
        Assert.Contains(typeof(PdfSaveOptions), found);
        Assert.Contains(typeof(RenderOptions), found);
    }

    [Theory]
    [MemberData(nameof(OptionsTypes))]
    public void Every_option_of_a_real_options_type_reaches_the_hosted_engine(string typeName)
    {
        Type type = Assemblies.Select(assembly => assembly.GetType(typeName)).First(static found => found is not null)!;

        IReadOnlyList<string> violations = type == typeof(PdfOptions)
            ? OptionsGuard.Check(type, OptionsScope.Engine, HostThroughAddBroadside, OptionsGuard.ExtensionMembers(type, Assemblies))
            : OptionsGuard.Check(type, OptionsScope.Call, HostThroughOptionsPattern(type));

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Every_exemption_names_a_member_that_still_exists()
    {
        foreach (string key in OptionsGuard.Exemptions.Keys)
        {
            string[] parts = key.Split('.');
            Type type = OptionsGuard.Discover(Assemblies).Single(candidate => candidate.Name == parts[0]);
            Assert.Contains(OptionsGuard.FluentMembers(type), method => method.Name == parts[1]);
        }
    }

    [Fact]
    public void A_fixture_with_a_value_option_that_cannot_bind_fails_the_guard()
    {
        IReadOnlyList<string> violations = OptionsGuard.Check(typeof(UnbindableValueOptions), OptionsScope.Engine, HostThroughOptionsPattern(typeof(UnbindableValueOptions)));

        string violation = Assert.Single(violations);
        Assert.Contains("WithLimit", violation, StringComparison.Ordinal);
        Assert.Contains("binds from configuration", violation, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fixture_that_exposes_non_bindable_state_in_a_public_property_fails_the_guard()
    {
        IReadOnlyList<string> violations = OptionsGuard.Check(typeof(StateInPropertyOptions), OptionsScope.Engine, HostThroughOptionsPattern(typeof(StateInPropertyOptions)));

        Assert.Contains(violations, static violation => violation.Contains("Logger", StringComparison.Ordinal) && violation.Contains("public property", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fixture_whose_member_does_not_chain_fails_the_guard()
    {
        IReadOnlyList<string> violations = OptionsGuard.Check(typeof(BrokenChainOptions), OptionsScope.Call, HostThroughOptionsPattern(typeof(BrokenChainOptions)));

        Assert.Contains(violations, static violation => violation.Contains("chain", StringComparison.Ordinal));
    }

    [Fact]
    public void A_fixture_with_bindable_values_and_state_in_Use_members_passes_the_guard()
    {
        IReadOnlyList<string> violations = OptionsGuard.Check(typeof(SoundOptions), OptionsScope.Engine, HostThroughOptionsPattern(typeof(SoundOptions)));

        Assert.Empty(violations);
    }

    [Fact]
    public void A_value_option_set_in_configuration_reaches_the_hosted_engines_options()
    {
        var values = new Dictionary<string, string?> { ["Broadside:ReadingMode"] = "Strict", ["Broadside:MaxImagePixels"] = "12345" };
        var services = new ServiceCollection();
        services.AddBroadside().AddOptions<PdfOptions>().Configure(options => options.UseLenient());
        services.Configure<PdfOptions>(new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("Broadside"));
        using ServiceProvider provider = services.BuildServiceProvider();

        PdfOptions options = provider.GetRequiredService<IOptions<PdfOptions>>().Value;

        Assert.Equal(PdfReadingMode.Strict, options.ReadingMode);
        Assert.Equal(12345, options.MaxImagePixels);
        Assert.NotNull(provider.GetRequiredService<PdfEngine>());
    }

    [Fact]
    public void No_public_mutable_static_state_exists_so_no_global_registry_appears()
    {
        var offenders = new List<string>();
        foreach (Type type in Assemblies.SelectMany(static assembly => assembly.GetExportedTypes()))
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (FieldInfo field in type.GetFields(flags).Where(static field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly))
            {
                if (field.IsLiteral || field.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null)
                {
                    continue;
                }

                if (!field.IsInitOnly)
                {
                    offenders.Add($"{type.FullName}.{field.Name}: a public static field that is not readonly");
                }
                else if (IsMutableContainer(field.FieldType))
                {
                    offenders.Add($"{type.FullName}.{field.Name}: a public static readonly {field.FieldType.Name}, whose contents anyone can change");
                }
            }

            foreach (PropertyInfo property in type.GetProperties(flags).Where(static property => property.GetMethod is { IsPublic: true } or { IsFamily: true }))
            {
                if (property.SetMethod is { IsPublic: true } or { IsFamily: true } or { IsFamilyOrAssembly: true })
                {
                    offenders.Add($"{type.FullName}.{property.Name}: a public static property with a setter");
                }
                else if (IsMutableContainer(property.PropertyType))
                {
                    offenders.Add($"{type.FullName}.{property.Name}: a public static {property.PropertyType.Name}, whose contents anyone can change");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    private static bool IsMutableContainer(Type type) =>
        type.IsArray
        || (type != typeof(string)
            && type.IsClass
            && !type.Name.Contains("ReadOnly", StringComparison.Ordinal)
            && !type.Name.Contains("Frozen", StringComparison.Ordinal)
            && !type.Name.Contains("Immutable", StringComparison.Ordinal)
            && (typeof(IList).IsAssignableFrom(type) || typeof(IDictionary).IsAssignableFrom(type)
                || type.GetInterfaces().Any(static i => i.IsGenericType && (i.GetGenericTypeDefinition() == typeof(ICollection<>) || i.GetGenericTypeDefinition() == typeof(IDictionary<,>)))));

    private static object HostThroughAddBroadside(Action<object> apply)
    {
        using ServiceProvider provider = new ServiceCollection().AddBroadside(options => apply(options)).BuildServiceProvider();
        PdfOptions value = provider.GetRequiredService<IOptions<PdfOptions>>().Value;

        // The engine the container builds from those options resolves too.
        _ = provider.GetRequiredService<PdfEngine>();
        return value;
    }

    private static Func<Action<object>, object> HostThroughOptionsPattern(Type type) => apply =>
    {
        // Per-call options never come out of the container; they are applied to a document's call, so the delegate runs on a new instance.
        object instance = OptionsGuard.Create(type)!;
        apply(instance);
        return instance;
    };
}

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Broadside.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Broadside.Rendering.Tests.Hosting;

/// <summary>Where an options type is applied, which decides how strict the guard is about its public properties.</summary>
internal enum OptionsScope
{
    /// <summary>Configures the engine: set by the <c>AddBroadside</c> delegate or bound from configuration, so every public property must bind.</summary>
    Engine,

    /// <summary>Handed to one call (a save, a render) on a document the engine opened: its properties may carry state.</summary>
    Call,
}

/// <summary>
/// The reflection guard of issue #239. It enumerates every <c>Use*</c> and <c>With*</c> member of an options type and proves that
/// each is reachable through the hosted engine's configuration delegate; that a value option (one whose arguments are all simple
/// values) changes a public property that binds from configuration; and that an engine-scope type keeps state that cannot bind
/// (filters, loggers, clocks, handlers) out of its public properties. It is generic, so an options type or member added later is
/// covered by the scan in <c>HostedOptionsGuardTests</c> without any per-ticket work.
/// </summary>
internal static class OptionsGuard
{
    private static readonly Dictionary<Type, object?[]> Known = new()
    {
        [typeof(ILoggerFactory)] = [NullLoggerFactory.Instance],
        [typeof(TimeProvider)] = [TimeProvider.System],
        [typeof(CancellationToken)] = [CancellationToken.None, new CancellationTokenSource().Token],
        [typeof(PdfCredentials)] = [new PdfPassword("secret")],
        [typeof(string)] = ["sample", "text/plain", null],
    };

    /// <summary>
    /// The value options that deliberately do not bind from configuration, each with the reason. Reachability and chaining are still
    /// checked for them. Adding an entry is a design decision to justify here; a stale entry fails <c>HostedOptionsGuardTests</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Exemptions { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PdfOptions.WithPassword"] = "A secret: PdfOptions keeps credentials out of its public properties on purpose, so configuration binding never sees a password; a host supplies it from its secret store through the delegate.",
        ["PdfOptions.WithPlatformImageDecoder"] = "A keyed toggle (decoder name, enabled) for decoders registered in code; configuration turns platform decoding on or off as a whole with PlatformImageDecoding.",
    };

    /// <summary>Gets the public types of <paramref name="assemblies"/> named <c>*Options</c> that have a fluent <c>Use*</c> or <c>With*</c> member.</summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>The types, in name order.</returns>
    public static IReadOnlyList<Type> Discover(params Assembly[] assemblies) =>
    [
        .. assemblies
            .SelectMany(static assembly => assembly.GetExportedTypes())
            .Where(static type => type is { IsClass: true, IsAbstract: false } && type.Name.EndsWith("Options", StringComparison.Ordinal))
            .Where(static type => FluentMembers(type).Any())
            .OrderBy(static type => type.FullName, StringComparer.Ordinal),
    ];

    /// <summary>Gets the fluent members of <paramref name="options"/>: public instance methods named <c>Use*</c> or <c>With*</c> that return the type.</summary>
    /// <param name="options">The options type.</param>
    /// <returns>The methods.</returns>
    public static IEnumerable<MethodInfo> FluentMembers(Type options) =>
        options.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => IsFluentName(method.Name) && method.ReturnType == options);

    /// <summary>Gets the extension methods from <paramref name="assemblies"/> named <c>Use*</c> or <c>With*</c> that extend <paramref name="options"/> and return it.</summary>
    /// <param name="options">The options type.</param>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>The methods.</returns>
    public static IEnumerable<MethodInfo> ExtensionMembers(Type options, params Assembly[] assemblies) =>
        assemblies.SelectMany(static assembly => assembly.GetExportedTypes())
            .Where(static type => type is { IsAbstract: true, IsSealed: true })
            .SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => IsFluentName(method.Name) && method.ReturnType == options
                && method.IsDefined(typeof(ExtensionAttribute), false)
                && method.GetParameters()[0].ParameterType == options);

    /// <summary>Checks <paramref name="options"/> and returns a description of each rule it breaks; empty when it passes.</summary>
    /// <param name="options">The options type, with a public parameterless constructor.</param>
    /// <param name="scope">Where the type is applied.</param>
    /// <param name="host">
    /// Applies a delegate to a new instance of the type the way the host does (for the engine's own options, through
    /// <c>services.AddBroadside(delegate)</c>) and returns the instance the host resolved.
    /// </param>
    /// <param name="extensions">Extension methods that extend the type; they compose its members, so only their reachability is checked.</param>
    /// <returns>The violations.</returns>
    public static IReadOnlyList<string> Check(Type options, OptionsScope scope, Func<Action<object>, object> host, IEnumerable<MethodInfo>? extensions = null)
    {
        var violations = new List<string>();
        if (Create(options) is null)
        {
            return [$"{options.Name}: no constructor the guard can call with sampled arguments."];
        }

        if (scope == OptionsScope.Engine)
        {
            foreach (PropertyInfo property in options.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(static property => !IsBindable(property.PropertyType)))
            {
                violations.Add($"{options.Name}.{property.Name}: state of type {property.PropertyType.Name} cannot bind from configuration, so it must not be a public property (set it with a Use* member).");
            }
        }

        foreach (MethodInfo method in FluentMembers(options))
        {
            CheckMember(options, method, host, violations);
        }

        foreach (MethodInfo extension in extensions ?? [])
        {
            string name = $"{options.Name}.{extension.Name}";
            object?[] arguments = [.. SampleCombinations(extension.GetParameters().Skip(1).Select(static parameter => parameter.ParameterType)).FirstOrDefault() ?? []];
            if (TryHost(host, extension, arguments, out _, out string? failure) is null)
            {
                violations.Add($"{name}: not reachable through the host delegate ({failure}).");
            }
        }

        return violations;
    }

    /// <summary>Gets whether configuration binding sets a property of <paramref name="type"/> from a string.</summary>
    /// <param name="type">The property type.</param>
    /// <returns><see langword="true"/> for primitives, enumerations, strings and a few simple structs, and nullable forms of them.</returns>
    public static bool IsBindable(Type type)
    {
        Type underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive || underlying.IsEnum
            || underlying == typeof(string) || underlying == typeof(decimal) || underlying == typeof(TimeSpan)
            || underlying == typeof(DateTimeOffset) || underlying == typeof(DateTime) || underlying == typeof(Guid) || underlying == typeof(Uri);
    }

    private static bool IsFluentName(string name) =>
        (name.StartsWith("Use", StringComparison.Ordinal) && name.Length > 3 && char.IsUpper(name[3]))
        || (name.StartsWith("With", StringComparison.Ordinal) && name.Length > 4 && char.IsUpper(name[4]));

    private static bool IsImmutable(Type type) =>
        !type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Any(static property => property.SetMethod is { IsPublic: true } setter && !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)));

    private static PropertyInfo[] BindableProperties(Type type) =>
    [
        .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property is { CanRead: true, CanWrite: true } && property.GetIndexParameters().Length == 0 && IsBindable(property.PropertyType)),
    ];

    private static Dictionary<string, object?> Snapshot(object instance) =>
        BindableProperties(instance.GetType()).ToDictionary(static property => property.Name, property => property.GetValue(instance), StringComparer.Ordinal);

    private static bool SameValues(Dictionary<string, object?> left, Dictionary<string, object?> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out object? other) && Equals(pair.Value, other));

    private static void CheckMember(Type options, MethodInfo method, Func<Action<object>, object> host, List<string> violations)
    {
        Type[] parameterTypes = [.. method.GetParameters().Select(static parameter => parameter.ParameterType)];
        string name = $"{options.Name}.{method.Name}({string.Join(", ", parameterTypes.Select(static type => type.Name))})";
        bool isValueOption = parameterTypes.All(IsBindable) && !Exemptions.ContainsKey($"{options.Name}.{method.Name}");
        bool sampled = false;
        bool reached = false;
        bool bound = false;
        string? lastFailure = null;

        foreach (object?[] arguments in SampleCombinations(parameterTypes))
        {
            sampled = true;

            // Reachable: the member runs inside the host's delegate and returns the instance it was given, so it chains.
            object? hosted = TryHost(host, method, arguments, out bool chained, out string? failure);
            if (hosted is null)
            {
                lastFailure = failure;
                continue;
            }

            reached = true;
            if (!chained && IsImmutable(options))
            {
                // An immutable value (it has no public setter) returns a changed copy: it is built in code, never configured or bound.
                isValueOption = false;
                break;
            }

            if (!chained)
            {
                violations.Add($"{name}: does not return the instance it was called on, so calls cannot chain.");
                return;
            }

            if (!isValueOption)
            {
                break;
            }

            // The effect: the member changes a bindable property of a new instance or of one whose properties all hold other values
            // (so a member that restores a default, such as UseLenient, shows too), and the host's instance shows the same effect.
            Dictionary<string, object?> viaHost = Snapshot(hosted);
            Dictionary<string, object?> freshStart = Snapshot(Create(options)!);
            foreach (object start in new[] { Create(options)!, Perturbed(options) })
            {
                Dictionary<string, object?> before = Snapshot(start);
                method.Invoke(start, arguments);
                Dictionary<string, object?> after = Snapshot(start);
                if (SameValues(before, after))
                {
                    continue;
                }

                if (SameValues(before, freshStart) && !SameValues(after, viaHost))
                {
                    violations.Add($"{name}: the instance the host resolved differs from one the member was applied to directly.");
                    return;
                }

                bound |= BindsFromConfiguration(options, after, violations, name);
            }
        }

        if (!sampled)
        {
            // An argument the guard cannot construct: the structural rules above still hold.
            return;
        }

        if (!reached)
        {
            violations.Add($"{name}: not reachable through the host delegate ({lastFailure}).");
        }
        else if (isValueOption && !bound)
        {
            violations.Add($"{name}: a value option must change a public property that binds from configuration, and no sampled argument did.");
        }
    }

    private static bool BindsFromConfiguration(Type options, Dictionary<string, object?> expected, List<string> violations, string name)
    {
        IEnumerable<KeyValuePair<string, string?>> pairs = expected
            .Where(static pair => pair.Value is not null)
            .Select(static pair => new KeyValuePair<string, string?>(pair.Key, Convert.ToString(pair.Value, CultureInfo.InvariantCulture)));
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
        object target = Create(options)!;
        configuration.Bind(target);
        if (!SameValues(expected, Snapshot(target)))
        {
            violations.Add($"{name}: the property values it sets do not bind back from configuration.");
            return false;
        }

        return true;
    }

    private static object? TryHost(Func<Action<object>, object> host, MethodInfo method, object?[] arguments, out bool chained, out string? failure)
    {
        failure = null;
        object? given = null;
        object? returned = null;
        try
        {
            object hosted = host(instance =>
            {
                given = instance;
                returned = method.IsStatic ? method.Invoke(null, [instance, .. arguments]) : method.Invoke(instance, arguments);
            });
            chained = returned is not null && ReferenceEquals(returned, given);
            return hosted;
        }
        catch (TargetInvocationException exception)
        {
            chained = false;
            failure = exception.InnerException?.Message ?? exception.Message;
            return null;
        }
    }

    private static object Perturbed(Type options)
    {
        object instance = Create(options)!;
        foreach (PropertyInfo property in BindableProperties(options))
        {
            object? current = property.GetValue(instance);
            foreach (object? sample in SamplesFor(property.PropertyType).Where(sample => !Equals(sample, current)))
            {
                try
                {
                    property.SetValue(instance, sample);
                    break;
                }
                catch (TargetInvocationException)
                {
                    // The setter rejected this value; try the next.
                }
            }
        }

        return instance;
    }

    private static IEnumerable<object?[]> SampleCombinations(IEnumerable<Type> parameterTypes)
    {
        IEnumerable<object?[]> combinations = [[]];
        foreach (Type type in parameterTypes)
        {
            object?[] choices = SamplesFor(type);
            if (choices.Length == 0)
            {
                return [];
            }

            combinations = [.. combinations.SelectMany(prefix => choices.Select(choice => (object?[])[.. prefix, choice]))];
        }

        return combinations;
    }

    /// <summary>Returns an empty array when <paramref name="type"/> is an array or a read-only sequence interface, otherwise <see langword="null"/>.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The empty collection.</returns>
    public static object? EmptyCollectionFor(Type type)
    {
        Type? element = type.IsArray
            ? type.GetElementType()
            : type is { IsInterface: true, IsGenericType: true } && type.GetGenericArguments().Length == 1
                && type.GetGenericTypeDefinition() is var definition
                && (definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>) || definition == typeof(IList<>) || definition == typeof(ICollection<>))
                    ? type.GetGenericArguments()[0]
                    : null;
        return element is null ? null : Array.CreateInstance(element, 0);
    }

    /// <summary>Creates an instance of <paramref name="type"/> with its parameterless constructor or, failing that, the first constructor or static factory whose arguments can be sampled.</summary>
    /// <param name="type">The options type.</param>
    /// <returns>The instance, or <see langword="null"/> when no constructor can be called.</returns>
    public static object? Create(Type type)
    {
        foreach (ConstructorInfo constructor in type.GetConstructors().OrderBy(static constructor => constructor.GetParameters().Length))
        {
            foreach (object?[] arguments in SampleCombinations(constructor.GetParameters().Select(static parameter => parameter.ParameterType)))
            {
                try
                {
                    return constructor.Invoke(arguments);
                }
                catch (TargetInvocationException)
                {
                    // This argument was rejected; try the next.
                }
            }
        }

        foreach (MethodInfo factory in type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(method => method.ReturnType == type).OrderBy(static method => method.GetParameters().Length))
        {
            foreach (object?[] arguments in SampleCombinations(factory.GetParameters().Select(static parameter => parameter.ParameterType)))
            {
                try
                {
                    return factory.Invoke(null, arguments);
                }
                catch (TargetInvocationException)
                {
                    // This argument was rejected; try the next.
                }
            }
        }

        return null;
    }

    private static object?[] SamplesFor(Type type)
    {
        if (Known.TryGetValue(type, out object?[]? known))
        {
            return known;
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return [.. SamplesFor(underlying), null];
        }

        if (type == typeof(bool))
        {
            return [true, false];
        }

        if (type.IsEnum)
        {
            return [.. Enum.GetValues(type).Cast<object>()];
        }

        if (type == typeof(int))
        {
            return [1, 2, 90, 1024, 4096];
        }

        if (type == typeof(long))
        {
            return [1L, 2L, 4096L, 1L << 20];
        }

        if (type == typeof(double))
        {
            return [1.0, 2.0, 0.5, 144.0];
        }

        if (type == typeof(TimeSpan))
        {
            return [TimeSpan.FromSeconds(1)];
        }

        if (type == typeof(DateTimeOffset))
        {
            return [new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)];
        }

        if (EmptyCollectionFor(type) is { } empty)
        {
            return [empty];
        }

        if (type.IsInterface)
        {
            return [SampleProxy.Create(type)];
        }

        if (type.IsValueType || (type is { IsClass: true, IsAbstract: false } && type.GetConstructor(Type.EmptyTypes) is not null))
        {
            return [Activator.CreateInstance(type)];
        }

        return [];
    }
}

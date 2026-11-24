using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Broadside.ApiGuard.Tests;

/// <summary>One breach of a rule: the member's key (what an allow list names) and why it breaks the rule.</summary>
internal sealed record Violation(string Key, string Reason)
{
    public override string ToString() => $"{Key}: {Reason}";
}

/// <summary>
/// The mechanical rules of the API review checklist (site/articles/api-review.md), each a function from public types to the
/// violations among them, so that the same rule runs over the shipping packages and over the failing fixtures.
/// </summary>
internal static partial class ApiRules
{
    // ---- Namespaces ------------------------------------------------------------------------------------------------------

    /// <summary>The areas the spec names (Implementation Decisions, "Namespaces"); the document model lives in the root.</summary>
    public static readonly string[] Areas =
    [
        "Objects", "Filters", "Fonts", "Images", "Graphics", "Content", "Rendering", "Text", "Layout", "Structure", "Forms",
        "Annotations", "Security", "Signatures", "Writing", "Diagnostics",
    ];

    /// <summary>
    /// The one namespace outside the root a type may use: dependency-injection registration (<c>AddBroadside</c>) follows the .NET
    /// convention of living in the namespace of <c>IServiceCollection</c>, so that it is found without a using directive.
    /// </summary>
    public const string DependencyInjectionNamespace = "Microsoft.Extensions.DependencyInjection";

    /// <summary>Every public type is in the root namespace or under one of the spec's areas.</summary>
    public static IEnumerable<Violation> Namespaces(IEnumerable<Type> types) =>
        from type in types
        let ns = type.Namespace ?? ""
        where !IsAllowedNamespace(ns)
        select new Violation(ApiSurface.FullName(type), $"namespace '{ns}' is not the root or one of the spec's areas");

    /// <summary>Whether a namespace is the root, one of the spec's areas (or under one), or the dependency-injection one.</summary>
    public static bool IsAllowedNamespace(string ns) =>
        ns is "Broadside" or DependencyInjectionNamespace || Areas.Any(area => IsInNamespace(ns, "Broadside." + area));

    /// <summary>Whether a namespace is <paramref name="parent"/> or under it.</summary>
    public static bool IsInNamespace(string ns, string parent) => ns == parent || ns.StartsWith(parent + ".", StringComparison.Ordinal);

    /// <summary>Whether a namespace belongs to an area whose types carry no Pdf or Cos prefix (Rendering, Text, Layout).</summary>
    public static bool IsUnprefixedArea(string ns) =>
        IsInNamespace(ns, "Broadside.Rendering") || IsInNamespace(ns, "Broadside.Text") || IsInNamespace(ns, "Broadside.Layout");

    // ---- Prefixes --------------------------------------------------------------------------------------------------------

    /// <summary>Whether a type name carries a prefix (<c>PdfPage</c>, <c>Pdf3DAnnotation</c>, <c>IPdfSigner</c> for Pdf; not <c>Pdfx</c>).</summary>
    public static bool HasPrefix(string name, string prefix) => StartsWithWord(name, prefix) || StartsWithWord(name, "I" + prefix);

    private static bool StartsWithWord(string name, string word) =>
        name.StartsWith(word, StringComparison.Ordinal) && name.Length > word.Length && (char.IsUpper(name[word.Length]) || char.IsDigit(name[word.Length]));

    /// <summary>
    /// Whether a type is an extension class named after the type it extends (<c>PdfImageRenderingExtensions</c>): a static class
    /// whose name ends in <c>Extensions</c>, which may carry the extended type's prefix in any area.
    /// </summary>
    public static bool IsExtensionClassName(string name, bool isStatic) => isStatic && name.EndsWith("Extensions", StringComparison.Ordinal);

    private static string SimpleName(Type type) => type.Name.Split('`')[0];

    private static bool InNamespace(Type type, string ns) => type.Namespace is { } actual && IsInNamespace(actual, ns);

    /// <summary>
    /// <c>Cos</c> is the prefix of the COS layer and only of it; <c>Pdf</c> marks the document model, which lives in the root and
    /// the areas; the Rendering, Text and Layout areas use no prefix (an extension class may still be named after the type it
    /// extends). A document-model type is recognized by what makes it one: a public view over a COS dictionary or stream (a visible
    /// instance property of a COS type). The contexts handed to extension-point implementations (<c>*Context</c>) expose the
    /// dictionary they work on without being views over it, and are not held to the Pdf prefix.
    /// </summary>
    public static IEnumerable<Violation> Prefixes(IEnumerable<Type> types)
    {
        foreach (Type type in types)
        {
            string name = SimpleName(type);
            bool cosNamespace = InNamespace(type, "Broadside.Objects");
            if (HasPrefix(name, "Cos") && !cosNamespace)
            {
                yield return new Violation(ApiSurface.FullName(type), "a Cos-prefixed type outside Broadside.Objects");
            }
            else if (cosNamespace && !HasPrefix(name, "Cos"))
            {
                yield return new Violation(ApiSurface.FullName(type), "a type in Broadside.Objects without the Cos prefix");
            }

            bool unprefixedArea = IsUnprefixedArea(type.Namespace ?? "");
            if (unprefixedArea && (HasPrefix(name, "Pdf") || HasPrefix(name, "Cos")) && !IsExtensionClassName(name, type.IsAbstract && type.IsSealed))
            {
                yield return new Violation(ApiSurface.FullName(type), "Rendering, Text and Layout types carry no Pdf or Cos prefix");
            }
            else if (!unprefixedArea && !cosNamespace && !type.IsNested && !HasPrefix(name, "Pdf") && IsDocumentModelView(type))
            {
                yield return new Violation(ApiSurface.FullName(type), "a view over a COS object (document model) without the Pdf prefix");
            }
        }
    }

    private static bool IsDocumentModelView(Type type) =>
        type.IsClass && !(type.IsAbstract && type.IsSealed) && !type.Name.EndsWith("Context", StringComparison.Ordinal)
        && ApiSurface.Properties(type).Any(p => !p.GetMethod!.IsStatic && IsCosContainer(p.PropertyType));

    private static bool IsCosContainer(Type type) =>
        type.Namespace == "Broadside.Objects" && type.Name is "CosDictionary" or "CosStream";

    // ---- Use* and With* --------------------------------------------------------------------------------------------------

    /// <summary>
    /// On an options type (a name ending in <c>Options</c>), every fluent method (one returning the options) is <c>Use*</c>, which
    /// selects an implementation, or <c>With*</c>, which sets a value: a <c>Use*</c> takes an implementation or nothing (not only
    /// values: numbers, enums, structs, strings), and a <c>With*</c> takes no implementation (an interface other than a collection
    /// or an <c>IProgress&lt;T&gt;</c> callback). Extension methods on an options type that return it are held to the same rule.
    /// </summary>
    public static IEnumerable<Violation> UseAndWith(IEnumerable<Type> types)
    {
        foreach (Type type in types)
        {
            foreach (MethodInfo method in ApiSurface.Methods(type).OfType<MethodInfo>())
            {
                ParameterInfo[] parameters = method.GetParameters();
                bool extension = method.IsStatic && method.IsDefined(typeof(ExtensionAttribute), inherit: false);
                Type? options = extension ? (parameters.Length > 0 ? parameters[0].ParameterType : null) : method.IsStatic ? null : type;
                if (options is null || !options.Name.EndsWith("Options", StringComparison.Ordinal) || method.ReturnType != options)
                {
                    continue;
                }

                ParameterInfo[] arguments = extension ? parameters[1..] : parameters;
                string key = ApiSurface.Describe(method);
                if (method.Name.StartsWith("Use", StringComparison.Ordinal))
                {
                    if (arguments.Length > 0 && arguments.All(p => IsValue(p.ParameterType)))
                    {
                        yield return new Violation(key, "Use* selects an implementation, but every argument is a value: name it With*");
                    }
                }
                else if (method.Name.StartsWith("With", StringComparison.Ordinal))
                {
                    if (arguments.Any(p => IsImplementation(p.ParameterType)))
                    {
                        yield return new Violation(key, "With* sets a value, but it takes an implementation: name it Use*");
                    }
                }
                else
                {
                    yield return new Violation(key, "a fluent options method that is neither Use* nor With*");
                }
            }
        }
    }

    private static bool IsValue(Type type) => type.IsValueType || type == typeof(string);

    private static bool IsImplementation(Type type) =>
        type.IsInterface && !typeof(IEnumerable).IsAssignableFrom(type) && !(type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IProgress<>));

    // ---- Sealed ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A public class is sealed (or static) unless it is designed for inheritance: it is abstract, declares a visible virtual or
    /// abstract member or a protected constructor, or another public type of the surface derives from it.
    /// </summary>
    public static IEnumerable<Violation> Sealed(IEnumerable<Type> types)
    {
        Type[] all = [.. types];
        HashSet<Type> bases = [.. all.Select(t => t.BaseType).OfType<Type>().Select(t => t.IsGenericType ? t.GetGenericTypeDefinition() : t)];
        return from type in all
               where type.IsClass && !type.IsSealed && !type.IsAbstract && !typeof(Delegate).IsAssignableFrom(type)
                   && !bases.Contains(type) && !DeclaresInheritanceContract(type)
               select new Violation(ApiSurface.FullName(type), "an unsealed class with no inheritance contract (no virtual or abstract member, no protected constructor, no public subclass)");
    }

    private static bool DeclaresInheritanceContract(Type type) =>
        ApiSurface.Methods(type).Any(m => m.IsFamily || m.IsFamilyOrAssembly || (m is MethodInfo { IsVirtual: true, IsFinal: false } method && method.GetBaseDefinition() == method))
        || ApiSurface.Properties(type).SelectMany(p => new[] { p.GetMethod, p.SetMethod }).OfType<MethodInfo>()
            .Any(m => ApiSurface.IsVisible(m) && m.IsVirtual && !m.IsFinal && m.GetBaseDefinition() == m && !m.IsDefined(typeof(CompilerGeneratedAttribute), false));

    // ---- Cancellation token last -----------------------------------------------------------------------------------------

    /// <summary>A method that takes a cancellation token takes exactly one, last, named <c>cancellationToken</c>.</summary>
    public static IEnumerable<Violation> CancellationTokenLast(IEnumerable<Type> types)
    {
        foreach (MethodBase method in types.SelectMany(ApiSurface.Methods))
        {
            ParameterInfo[] parameters = method.GetParameters();
            int count = parameters.Count(p => p.ParameterType == typeof(CancellationToken));
            if (count == 0)
            {
                continue;
            }

            ParameterInfo last = parameters[^1];
            if (count > 1 || last.ParameterType != typeof(CancellationToken))
            {
                yield return new Violation(ApiSurface.Describe(method), "the cancellation token is not the last parameter");
            }
            else if (last.Name != "cancellationToken")
            {
                yield return new Violation(ApiSurface.Describe(method), $"the cancellation token is named '{last.Name}', not 'cancellationToken'");
            }
        }
    }

    // ---- Async -----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A method is named <c>*Async</c> exactly when it returns an awaitable (<c>Task</c>, <c>ValueTask</c>, their generic forms, or
    /// <c>IAsyncEnumerable&lt;T&gt;</c>), and an awaitable method takes a cancellation token. <c>DisposeAsync</c> and overrides
    /// of members declared outside the surface follow their base's signature and are not checked.
    /// </summary>
    public static IEnumerable<Violation> AsyncNaming(IEnumerable<Type> types)
    {
        foreach (MethodInfo method in types.SelectMany(ApiSurface.Methods).OfType<MethodInfo>())
        {
            if (method.Name == "DisposeAsync" || method.GetBaseDefinition() != method)
            {
                continue;
            }

            bool awaitable = IsAwaitable(method.ReturnType);
            bool named = method.Name.EndsWith("Async", StringComparison.Ordinal);
            string key = ApiSurface.Describe(method);
            if (named && !awaitable)
            {
                yield return new Violation(key, "named *Async but returns no Task, ValueTask or IAsyncEnumerable");
            }
            else if (awaitable && !named)
            {
                yield return new Violation(key, "returns an awaitable but is not named *Async");
            }
            else if (awaitable && !method.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)))
            {
                yield return new Violation(key, "an asynchronous method without a cancellation token");
            }
        }
    }

    private static bool IsAwaitable(Type type)
    {
        Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        return definition == typeof(Task) || definition == typeof(Task<>) || definition == typeof(ValueTask) || definition == typeof(ValueTask<>)
            || definition == typeof(IAsyncEnumerable<>);
    }

    // ---- Optional parameters ---------------------------------------------------------------------------------------------

    /// <summary>
    /// An overloaded method or constructor has no optional parameters, except a trailing <c>CancellationToken cancellationToken =
    /// default</c>: options are overloads, never a defaulted parameter beside an overload.
    /// </summary>
    public static IEnumerable<Violation> NoOptionalParametersOnOverloads(IEnumerable<Type> types)
    {
        foreach (Type type in types)
        {
            foreach (IGrouping<string, MethodBase> overloads in ApiSurface.Methods(type).GroupBy(m => m.IsConstructor ? ".ctor" : m.Name))
            {
                if (overloads.Count() < 2)
                {
                    continue;
                }

                foreach (MethodBase method in overloads)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    IEnumerable<ParameterInfo> optional = parameters.Where((p, i) => p.IsOptional && !(i == parameters.Length - 1 && p.ParameterType == typeof(CancellationToken)));
                    if (optional.Any())
                    {
                        yield return new Violation(ApiSurface.Describe(method), $"an overloaded method with optional parameter(s) {string.Join(", ", optional.Select(p => p.Name))}");
                    }
                }
            }
        }
    }

    // ---- Read-only collections -------------------------------------------------------------------------------------------

    private static readonly Type[] MutableCollections =
    [
        typeof(List<>), typeof(Dictionary<,>), typeof(HashSet<>), typeof(SortedSet<>), typeof(SortedDictionary<,>), typeof(SortedList<,>),
        typeof(LinkedList<>), typeof(Queue<>), typeof(Stack<>), typeof(Collection<>), typeof(ObservableCollection<>),
        typeof(IList<>), typeof(ICollection<>), typeof(IDictionary<,>), typeof(ISet<>),
        typeof(ArrayList), typeof(Hashtable), typeof(IList), typeof(IDictionary),
    ];

    /// <summary>
    /// What the surface hands out (property and field types, method results, out parameters) is never a mutable collection type
    /// (<c>List&lt;T&gt;</c>, <c>Dictionary&lt;K, V&gt;</c>, <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>, ...), at any depth of
    /// generic arguments, and a property is never an array: expose <c>IReadOnlyList&lt;T&gt;</c>, <c>IReadOnlyDictionary</c>,
    /// <c>IEnumerable&lt;T&gt;</c> or a memory type.
    /// </summary>
    public static IEnumerable<Violation> ReadOnlyCollections(IEnumerable<Type> types)
    {
        foreach (Type type in types)
        {
            // A member implementing an interface has the interface's signature (CosDictionary.Keys is IDictionary's ICollection).
            HashSet<MethodInfo> implementing = type.IsInterface ? [] : [.. type.GetInterfaces().SelectMany(i => type.GetInterfaceMap(i).TargetMethods)];
            foreach (PropertyInfo property in ApiSurface.Properties(type).Where(p => p.GetMethod is null || !implementing.Contains(p.GetMethod)))
            {
                if (property.PropertyType.IsArray)
                {
                    yield return new Violation(ApiSurface.Describe(property), $"a property of array type {ApiSurface.Name(property.PropertyType)}");
                }
                else if (FindMutable(property.PropertyType) is { } mutable)
                {
                    yield return new Violation(ApiSurface.Describe(property), $"exposes the mutable collection {ApiSurface.Name(mutable)}");
                }
            }

            foreach (FieldInfo field in ApiSurface.Fields(type))
            {
                if (FindMutable(field.FieldType) is { } mutable)
                {
                    yield return new Violation(ApiSurface.Describe(field), $"exposes the mutable collection {ApiSurface.Name(mutable)}");
                }
            }

            foreach (MethodBase method in ApiSurface.Methods(type).Where(m => m is not MethodInfo info || !implementing.Contains(info)))
            {
                IEnumerable<Type> outputs = method.GetParameters().Where(p => p.IsOut).Select(p => p.ParameterType.GetElementType()!);
                if (method is MethodInfo info)
                {
                    outputs = outputs.Prepend(info.ReturnType);
                }

                if (outputs.Select(FindMutable).OfType<Type>().FirstOrDefault() is { } mutable)
                {
                    yield return new Violation(ApiSurface.Describe(method), $"returns the mutable collection {ApiSurface.Name(mutable)}");
                }
            }
        }
    }

    private static Type? FindMutable(Type type)
    {
        if (type.IsByRef || type.IsArray)
        {
            return FindMutable(type.GetElementType()!);
        }

        Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        if (MutableCollections.Contains(definition))
        {
            return type;
        }

        return type.IsGenericType ? type.GetGenericArguments().Select(FindMutable).OfType<Type>().FirstOrDefault() : null;
    }

    // ---- Extension points ------------------------------------------------------------------------------------------------

    /// <summary>
    /// A public abstract class a consumer can derive from (it has a public or protected constructor) is an extension point that is
    /// not an interface: the spec makes every extension point an interface with a managed default.
    /// </summary>
    public static IEnumerable<Violation> ExtensionPointsAreInterfaces(IEnumerable<Type> types) =>
        from type in types
        where type.IsClass && type.IsAbstract && !type.IsSealed
            && type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Any(c => c.IsPublic || c.IsFamily || c.IsFamilyOrAssembly)
        select new Violation(ApiSurface.FullName(type), "an abstract class consumers can derive from: an extension point that is not an interface");

    /// <summary>
    /// Each named extension point is an interface, the shipping assemblies hold a concrete implementation of it (the managed
    /// default; it need not be public), and an options type registers it through a <c>Use*</c> method.
    /// </summary>
    public static IEnumerable<Violation> ExtensionPointsHaveManagedDefaults(IEnumerable<Type> extensionPoints, IEnumerable<Assembly> assemblies)
    {
        Assembly[] all = [.. assemblies];
        Type[] implementations = [.. all.SelectMany(LoadableTypes).Where(t => t.IsClass && !t.IsAbstract)];
        Type[] registered = [.. all.SelectMany(ApiSurface.PublicTypes).SelectMany(ApiSurface.Methods).OfType<MethodInfo>()
            .Where(m => m.Name.StartsWith("Use", StringComparison.Ordinal) && m.ReturnType.Name.EndsWith("Options", StringComparison.Ordinal))
            .SelectMany(m => m.GetParameters()).Select(p => p.ParameterType)];
        foreach (Type point in extensionPoints)
        {
            if (!point.IsInterface)
            {
                yield return new Violation(ApiSurface.FullName(point), "an extension point that is not an interface");
            }
            else if (!implementations.Any(point.IsAssignableFrom))
            {
                yield return new Violation(ApiSurface.FullName(point), "an extension point with no managed default in the shipping packages");
            }
            else if (!registered.Any(p => p.IsAssignableFrom(point)))
            {
                yield return new Violation(ApiSurface.FullName(point), "an extension point no options type registers through a Use* method");
            }
        }
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            return e.Types.OfType<Type>();
        }
    }

    // ---- Glossary --------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Verbs CONTEXT.md lists under "Avoid" for the capabilities (Read, Write, Edit, Render): no method name starts with one. Only
    /// the first word of a method (its verb) is checked, because "export" and "modify" also name PDF entries (a field's export
    /// value, the modify permission). "Paint" and "draw" name the content operators the API mirrors (the path-painting
    /// operators, PdfCanvas) and are left to review.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> AvoidedVerbs = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Load"] = "Open (Read)",
        ["Import"] = "Open (Read)",
        ["Generate"] = "Create (Write)",
        ["Export"] = "Save or Extract",
        ["Manipulate"] = "Edit",
        ["Modify"] = "Edit",
        ["Rasterize"] = "Render",
    };

    /// <summary>
    /// Nouns CONTEXT.md lists under "Avoid" that would only appear in a type name as a synonym for a glossary term. The rest of the
    /// avoid lists are ordinary words (view, target, range, row) or glossary terms elsewhere (decoder in "platform image decoder",
    /// rasterizer in "software rasterizer") and are left to review.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> AvoidedNouns = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Codec"] = "Filter",
        ["Compressor"] = "Filter",
        ["Typeface"] = "Font program",
        ["Base14"] = "Standard 14",
        ["Renderer"] = "Backend",
        ["Driver"] = "Backend",
        ["Plugin"] = "Extension point",
        ["Provider"] = "Extension point",
        ["Hook"] = "Extension point",
        ["Tolerant"] = "Lenient",
        ["Pedantic"] = "Strict",
        ["Dom"] = "Document model",
    };

    [GeneratedRegex("[A-Z]+(?![a-z])|[A-Z]?[a-z0-9]+")]
    private static partial Regex WordPattern();

    /// <summary>The words of a PascalCase identifier: <c>LoadFromStream</c> is Load, From, Stream.</summary>
    public static IEnumerable<string> Words(string identifier) => WordPattern().Matches(identifier).Select(m => m.Value);

    /// <summary>Why a type name breaks the glossary, or <see langword="null"/>.</summary>
    public static string? AvoidedInTypeName(string name) =>
        Words(name).FirstOrDefault(AvoidedNouns.ContainsKey) is { } word ? $"'{word}' is a word the glossary avoids: say {AvoidedNouns[word]}" : null;

    /// <summary>Why a method name breaks the glossary, or <see langword="null"/>.</summary>
    public static string? AvoidedInMethodName(string name) =>
        Words(name).FirstOrDefault() is { } verb && AvoidedVerbs.TryGetValue(verb, out string? say) ? $"'{verb}' is a verb the glossary avoids: say {say}" : null;

    /// <summary>No type name uses a noun, and no method name starts with a verb, that the glossary says to avoid.</summary>
    public static IEnumerable<Violation> GlossaryWords(IEnumerable<Type> types)
    {
        foreach (Type type in types)
        {
            if (AvoidedInTypeName(SimpleName(type)) is { } typeReason)
            {
                yield return new Violation(ApiSurface.FullName(type), typeReason);
            }

            foreach (MethodBase method in ApiSurface.Methods(type).Where(m => !m.IsConstructor && !m.IsSpecialName))
            {
                if (AvoidedInMethodName(method.Name) is { } reason)
                {
                    yield return new Violation(ApiSurface.Describe(method), reason);
                }
            }
        }
    }
}

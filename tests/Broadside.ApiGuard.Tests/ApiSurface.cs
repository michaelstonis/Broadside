using System.Reflection;
using System.Runtime.CompilerServices;

namespace Broadside.ApiGuard.Tests;

/// <summary>What a consumer of a package can see, and the names the guards report it by.</summary>
internal static class ApiSurface
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>The public types of an assembly, nested public types included.</summary>
    public static IEnumerable<Type> PublicTypes(Assembly assembly) => assembly.GetExportedTypes();

    /// <summary>Whether a consumer can see the member: public, or protected on a type that can be derived from.</summary>
    public static bool IsVisible(MethodBase method) =>
        method.IsPublic || ((method.IsFamily || method.IsFamilyOrAssembly) && method.DeclaringType is { IsSealed: false });

    /// <summary>The visible constructors and ordinary methods a type declares (accessors and compiler-generated members excluded).</summary>
    public static IEnumerable<MethodBase> Methods(Type type) =>
        type.GetConstructors(Declared).Cast<MethodBase>()
            .Concat(type.GetMethods(Declared).Where(m => !m.IsSpecialName || m.Name.StartsWith("op_", StringComparison.Ordinal)))
            .Where(m => IsVisible(m) && !m.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false));

    /// <summary>The visible properties a type declares.</summary>
    public static IEnumerable<PropertyInfo> Properties(Type type) =>
        type.GetProperties(Declared).Where(p => p.GetMethod is { } getter ? IsVisible(getter) : p.SetMethod is { } setter && IsVisible(setter));

    /// <summary>The visible fields a type declares (enum members excluded).</summary>
    public static IEnumerable<FieldInfo> Fields(Type type) =>
        type.IsEnum ? [] : type.GetFields(Declared).Where(f => f.IsPublic || ((f.IsFamily || f.IsFamilyOrAssembly) && !type.IsSealed));

    /// <summary>A type's name as C# writes it, without namespaces: <c>IReadOnlyList&lt;PdfPage&gt;</c>, <c>PdfOptions.Builder</c>.</summary>
    public static string Name(Type type)
    {
        if (type.IsByRef)
        {
            return Name(type.GetElementType()!) + "&";
        }

        if (type.IsArray)
        {
            return Name(type.GetElementType()!) + "[]";
        }

        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Name(underlying) + "?";
        }

        string name = type.Name;
        int tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
        {
            name = name[..tick];
        }

        if (type.IsGenericType)
        {
            // Generic arguments of the enclosing types are carried by the nested type; show only this type's own.
            int inherited = type.DeclaringType is { IsGenericType: true } outer ? outer.GetGenericArguments().Length : 0;
            Type[] arguments = type.GetGenericArguments()[inherited..];
            if (arguments.Length > 0)
            {
                name += "<" + string.Join(", ", arguments.Select(Name)) + ">";
            }
        }

        return type.IsNested && !type.IsGenericParameter ? Name(type.DeclaringType!) + "." + name : name;
    }

    /// <summary>A type's full name as C# writes it: <c>Broadside.PdfDocument</c>, <c>Broadside.PdfOptions.Builder</c>.</summary>
    public static string FullName(Type type) => string.IsNullOrEmpty(type.Namespace) ? Name(type) : type.Namespace + "." + Name(type);

    /// <summary>A member's key in the guards' reports and allow lists: <c>Broadside.PdfDocument.Save(Stream, PdfSaveOptions)</c>.</summary>
    public static string Describe(MemberInfo member) => member switch
    {
        Type type => FullName(type),
        MethodBase method => FullName(method.DeclaringType!) + "." + (method.IsConstructor ? method.DeclaringType!.Name.Split('`')[0] : method.Name)
            + "(" + string.Join(", ", method.GetParameters().Select(p => Name(p.ParameterType))) + ")",
        _ => FullName(member.DeclaringType!) + "." + member.Name,
    };
}

using System.Reflection;
using Broadside.Objects;

namespace Broadside.Rendering.Tests.Hosting;

/// <summary>
/// A stand-in for an extension-point interface (a filter, a handler, a resolver) that the options guard passes to a <c>Use*</c>
/// member: every call answers a default, a name for the <c>Name</c> and <c>Filter</c> properties registration reads.
/// </summary>
public class SampleProxy : DispatchProxy
{
    /// <summary>Creates a proxy that implements <paramref name="interfaceType"/>.</summary>
    /// <param name="interfaceType">The interface.</param>
    /// <returns>The proxy.</returns>
    internal static object Create(Type interfaceType) =>
        typeof(DispatchProxy).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(static method => method.Name == nameof(DispatchProxy.Create) && method.GetGenericArguments().Length == 2)
            .MakeGenericMethod(interfaceType, typeof(SampleProxy))
            .Invoke(null, null)!;

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        Type? type = targetMethod?.ReturnType;
        if (type is null || type == typeof(void))
        {
            return null;
        }

        if (type == typeof(CosName))
        {
            return new CosName("Sample");
        }

        if (type == typeof(string))
        {
            return "sample";
        }

        if (OptionsGuard.EmptyCollectionFor(type) is { } empty)
        {
            return empty;
        }

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}

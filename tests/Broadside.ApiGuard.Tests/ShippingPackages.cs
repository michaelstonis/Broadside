using System.Reflection;

namespace Broadside.ApiGuard.Tests;

/// <summary>
/// The packages that ship as v1 and whose public surface the API guards check: the one list the guards, the review checklist and
/// the API freeze read. Every project under <c>src/</c> is packed and ships as 1.0.0, the viewer packages included.
/// </summary>
internal static class ShippingPackages
{
    /// <summary>The packages built on every OS: their assemblies are referenced by this project and checked by reflection.</summary>
    public static readonly string[] Reflected =
    [
        "Broadside",
        "Broadside.Rendering",
        "Broadside.Rendering.Skia",
        "Broadside.Rendering.WebGL",
        "Broadside.Fonts.Cmaps",
        "Broadside.Fonts.Standard14",
        "Broadside.Layout",
        "Broadside.Viewer",
        "Broadside.Viewer.Avalonia",
        "Broadside.Viewer.Blazor",
    ];

    /// <summary>
    /// The native backends and the viewer shells over platform UI frameworks, which build only where their workloads or SDKs are
    /// installed (not in the core gate), so they are checked from their <c>PublicAPI.*.txt</c> files by the rules a signature line
    /// can show.
    /// </summary>
    public static readonly string[] Platform =
    [
        "Broadside.Rendering.Android",
        "Broadside.Rendering.CoreGraphics",
        "Broadside.Rendering.Direct2D",
        "Broadside.Viewer.Maui",
        "Broadside.Viewer.Uno",
        "Broadside.Viewer.WinUI",
        "Broadside.Viewer.Wpf",
    ];

    /// <summary>Every v1 shipping package.</summary>
    public static IEnumerable<string> All => Reflected.Concat(Platform);

    private static readonly Lazy<Assembly[]> LoadedAssemblies = new(() => [.. Reflected.Select(name => Assembly.Load(new AssemblyName(name)))]);

    /// <summary>The assemblies of <see cref="Reflected"/>.</summary>
    public static IReadOnlyList<Assembly> Assemblies => LoadedAssemblies.Value;

    /// <summary>Every public type of the reflected shipping packages.</summary>
    public static IEnumerable<Type> PublicTypes => Assemblies.SelectMany(ApiSurface.PublicTypes);

    /// <summary>The repository root (the directory holding <c>Broadside.slnx</c>).</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Broadside.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No Broadside.slnx above '{AppContext.BaseDirectory}'.");
    }
}

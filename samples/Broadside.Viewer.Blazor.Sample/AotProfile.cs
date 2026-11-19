using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;

namespace Broadside.Viewer.Blazor.Sample;

/// <summary>
/// Records the AOT profile of the <c>--hot-aot</c> build (issue #547). A build published with <c>publish-static.sh --aot-profiler</c>
/// runs Mono's AOT profiler, which notes every method the session runs. Calling <see cref="Write"/> (from the browser tests,
/// through <c>getAssemblyExports</c>) makes the profiler hand its data to the runtime, which stores it in
/// <c>getDotnetRuntime(0).INTERNAL.aotProfileData</c>; <c>wwwroot/js/start.js</c> names <see cref="WriteBroadsideAotProfile"/> and the receiving method
/// to the profiler. In any other build, <see cref="Write"/> does nothing.
/// </summary>
public static partial class AotProfile
{
    /// <summary>Writes the profile of the session so far.</summary>
    [JSExport]
    [DynamicDependency("DumpAotProfileData", "System.Runtime.InteropServices.JavaScript.JavaScriptExports", "System.Runtime.InteropServices.JavaScript")]
    public static void Write() => WriteBroadsideAotProfile();

    // The profiler writes its data when this method is first compiled, which is its first call. It matches the method by name only,
    // whatever the class, so the name is one no other method has.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void WriteBroadsideAotProfile()
    {
    }
}

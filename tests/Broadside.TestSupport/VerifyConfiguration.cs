using System.Runtime.CompilerServices;

namespace Broadside.TestSupport;

/// <summary>
/// Configures Verify for every test project that references this library. Snapshots live in a <c>Snapshots/</c> folder next to the
/// test source file: <c>*.verified.*</c> files are committed, <c>*.received.*</c> files are ignored by <c>.gitignore</c>.
/// </summary>
public static class VerifyConfiguration
{
    /// <summary>Runs once when this module loads. Test projects do not need to call it, but may, to force the configuration early.</summary>
    [ModuleInitializer]
    public static void Initialize()
    {
        // Verify 33 moved path derivation from VerifierSettings to the test-framework adapter (VerifyXunit.Verifier).
        Verifier.UseSourceFileRelativeDirectory("Snapshots");
    }
}

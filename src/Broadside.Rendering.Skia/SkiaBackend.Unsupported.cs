using Broadside.Diagnostics;

namespace Broadside.Rendering.Skia;

public sealed partial class SkiaBackend
{
    /// <summary>
    /// What this backend approximates, hands to the software rasterizer or leaves out, each reported once per kind per
    /// render (ADR 0005: nothing is skipped silently). The diagnostics are shared immutable instances, so reporting allocates nothing;
    /// the replayers of pattern cells report through the backend's own.
    /// </summary>
    private sealed partial class Replayer
    {
        private int _reported;

        /// <summary>Gets the diagnostics of the current or last render.</summary>
        public List<Diagnostic> Diagnostics { get; } = new(4);

        private void Report(int kind, Diagnostic diagnostic)
        {
            if (_owner is not null)
            {
                _owner.Report(kind, diagnostic);
                return;
            }

            if ((_reported & kind) == 0)
            {
                _reported |= kind;
                Diagnostics.Add(diagnostic);
            }
        }
    }
}

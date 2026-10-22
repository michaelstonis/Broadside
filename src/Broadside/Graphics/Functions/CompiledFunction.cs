namespace Broadside.Graphics.Functions;

/// <summary>An evaluator and the containers it was compiled from; stale once any of them has changed or an object was added or removed.</summary>
/// <remarks>
/// ISO 32000-2 §7.10 and §7.3.10. A removed object changes what a reference in an unchanged container resolves to, so the
/// document's <c>ObjectsVersion</c> at compilation is recorded too (issue #303). Immutable apart from the once-only runtime report flag.
/// </remarks>
internal sealed class CompiledFunction(FunctionEvaluator evaluator, FunctionDependency[] dependencies, PdfDocument document, int objectsVersion)
{
    private int _reported;

    /// <summary>Gets the evaluator.</summary>
    public FunctionEvaluator Evaluator { get; } = evaluator;

    /// <summary>Gets a value indicating whether no object was added or removed and every container the evaluator was compiled from is unchanged.</summary>
    public bool IsCurrent
    {
        get
        {
            if (document.ObjectsVersion != objectsVersion)
            {
                return false;
            }

            foreach (FunctionDependency dependency in dependencies)
            {
                if (!dependency.IsCurrent)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Claims the one runtime report this compilation makes.</summary>
    /// <returns><see langword="true"/> for the first caller only.</returns>
    public bool ClaimReport() => Interlocked.Exchange(ref _reported, 1) == 0;
}

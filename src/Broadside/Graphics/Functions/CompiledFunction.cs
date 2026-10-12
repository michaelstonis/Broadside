namespace Broadside.Graphics.Functions;

/// <summary>An evaluator and the containers it was compiled from; stale once any of them has changed.</summary>
/// <remarks>ISO 32000-2 §7.10; ADR 0004. Immutable apart from the once-only runtime report flag.</remarks>
internal sealed class CompiledFunction(FunctionEvaluator evaluator, FunctionDependency[] dependencies)
{
    private int _reported;

    /// <summary>Gets the evaluator.</summary>
    public FunctionEvaluator Evaluator { get; } = evaluator;

    /// <summary>Gets a value indicating whether every container the evaluator was compiled from is unchanged.</summary>
    public bool IsCurrent
    {
        get
        {
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

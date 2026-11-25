// Fixtures for the API guards: each "Bad*" member breaks exactly the rule its region names, and each "Good*" one follows it.
// The analyzers that already catch some of these breaches at build time are silenced here, since breaking the rule is the point.
#pragma warning disable IDE0130, CA1002, CA1024, CA1040, CA1030, CA1033, CA1068, CA1716, CA1819, CA1822, CA1852, CA2227, IDE0060, RS0026, RS0027

using Broadside.Fonts;
using Broadside.Objects;

namespace Broadside.Annotations.GuardFixtures;

// ---- Prefixes: document-model views carry Pdf, nothing else carries Cos ----------------------------------------------------

public sealed class BadStampView
{
    public CosDictionary Dictionary => throw new NotSupportedException();
}

public sealed class PdfGoodStampView
{
    public CosDictionary Dictionary => throw new NotSupportedException();
}

public sealed class GoodStampContext
{
    public CosDictionary Dictionary => throw new NotSupportedException();
}

public sealed class CosBadOutsideObjects;

// ---- Use* and With* ----------------------------------------------------------------------------------------------------------

public sealed class FixtureOptions
{
    public FixtureOptions BadSetScale(double scale) => this;

    public FixtureOptions UseBadScale(double scale) => this;

    public FixtureOptions WithBadResolver(IFontResolver resolver) => this;

    public FixtureOptions WithGoodScale(double scale) => this;

    public FixtureOptions UseGoodResolver(IFontResolver resolver) => this;

    public FixtureOptions UseGoodDefaults() => this;

    public FixtureOptions WithGoodProgress(IProgress<double> progress) => this;

    public FixtureOptions WithGoodNames(IEnumerable<string> names) => this;
}

public static class FixtureOptionsExtensions
{
    public static FixtureOptions BadExtension(this FixtureOptions options) => options;

    public static FixtureOptions UseGoodExtension(this FixtureOptions options) => options;
}

// ---- Sealed ------------------------------------------------------------------------------------------------------------------

public class BadUnsealed;

public sealed class GoodSealed;

public class GoodVirtualMember
{
    public virtual void Hook()
    {
    }
}

public class GoodProtectedConstructor
{
    protected GoodProtectedConstructor()
    {
    }
}

public class GoodBaseOfPublicType;

public sealed class GoodDerived : GoodBaseOfPublicType;

public static class GoodStatic;

// ---- Cancellation token last -------------------------------------------------------------------------------------------------

public sealed class TokenFixtures
{
    public void BadTokenFirst(CancellationToken cancellationToken, int count)
    {
    }

    public void BadTokenName(int count, CancellationToken token)
    {
    }

    public void GoodTokenLast(int count, CancellationToken cancellationToken)
    {
    }
}

// ---- Async -------------------------------------------------------------------------------------------------------------------

public sealed class AsyncFixtures
{
    public Task BadUnnamed(CancellationToken cancellationToken) => Task.CompletedTask;

    public int BadNotAwaitableAsync() => 0;

    public Task BadNoTokenAsync() => Task.CompletedTask;

    public ValueTask<int> GoodAsync(CancellationToken cancellationToken) => ValueTask.FromResult(0);

    public IAsyncEnumerable<int> GoodStreamAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}

// ---- Optional parameters on overloads ----------------------------------------------------------------------------------------

public sealed class OverloadFixtures
{
    public void Place(int x)
    {
    }

    public void Place(int x, int y = 0)
    {
    }

    public Task SaveAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task SaveAsync(Stream stream, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Lone(int x = 0)
    {
    }
}

// ---- Read-only collections ---------------------------------------------------------------------------------------------------

public sealed class CollectionFixtures
{
    public List<int> BadList { get; } = [];

    public int[] BadArray { get; } = [];

    public IReadOnlyList<IList<string>> BadNested { get; } = [];

    public IReadOnlyList<int> GoodList { get; } = [];

    public ReadOnlyMemory<byte> GoodMemory { get; }

    public IDictionary<string, int> BadResult() => throw new NotSupportedException();

    public bool BadOut(out Dictionary<string, int> map) => throw new NotSupportedException();

    public int[] GoodArrayResult() => [];
}

// ---- Extension points --------------------------------------------------------------------------------------------------------

public abstract class BadDerivableBase
{
    protected BadDerivableBase()
    {
    }
}

public abstract class GoodClosedBase
{
    internal GoodClosedBase()
    {
    }
}

public interface IGoodExtensionPoint;

public interface IBadNoDefault;

public interface IBadUnregistered;

public abstract class BadAbstractExtensionPoint;

internal sealed class GoodDefault : IGoodExtensionPoint;

internal sealed class UnregisteredDefault : IBadUnregistered;

public sealed class ExtensionFixtureOptions
{
    public ExtensionFixtureOptions UseGood(IGoodExtensionPoint point) => this;
}

// ---- Glossary ----------------------------------------------------------------------------------------------------------------

public sealed class BadFontProvider
{
    public void LoadBad()
    {
    }

    public void GoodExtractExportValue()
    {
    }

    public string ExportValue => "";
}

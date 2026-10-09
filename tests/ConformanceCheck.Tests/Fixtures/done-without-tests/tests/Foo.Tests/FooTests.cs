using Xunit;

namespace Foo.Tests;

// Fixture for tools/ConformanceCheck: scanned by the checker, never compiled.
public class FooTests
{
    [Fact]
    public void Parses()
    {
    }

    [Theory]
    [InlineData(1)]
    public void Rejects(int value)
    {
    }

    // Not a test: no [Fact] or [Theory], so the conformance map may not cite it.
    public void Helper()
    {
    }
}

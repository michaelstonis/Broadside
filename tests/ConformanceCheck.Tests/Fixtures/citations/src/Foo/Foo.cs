namespace Foo;

// Fixture for tools/ConformanceCheck: scanned by the checker, never compiled. Its XML documentation is xmldoc/Foo.xml.
public sealed class Foo
{
    public sealed class Nested
    {
    }
}

public sealed class Generic<T>
{
}

public readonly record struct FooOptions(int Value);

internal sealed class Parser
{
}

using Microsoft.Extensions.Logging;

namespace Broadside.Rendering.Tests.Hosting;

/// <summary>A value option, <c>WithLimit</c>, that keeps its value in a field: configuration can never set it.</summary>
internal sealed class UnbindableValueOptions
{
    private int _limit;

    public int Used => _limit;

    public UnbindableValueOptions WithLimit(int limit)
    {
        _limit = limit;
        return this;
    }
}

/// <summary>State a configuration source cannot bind, exposed as a public property.</summary>
internal sealed class StateInPropertyOptions
{
    public ILoggerFactory? Logger { get; private set; }

    public StateInPropertyOptions UseLogger(ILoggerFactory logger)
    {
        Logger = logger;
        return this;
    }
}

/// <summary>A member that returns a new instance, so a chain would drop the earlier calls.</summary>
internal sealed class BrokenChainOptions
{
    public bool Flag { get; set; }

    public BrokenChainOptions WithFlag(bool flag) => new() { Flag = flag || Flag };
}

/// <summary>The shape every real options type follows: values bind, state is code.</summary>
internal sealed class SoundOptions
{
    private TimeProvider? _clock;

    public int Limit { get; set; }

    public bool Enabled { get; set; } = true;

    public SoundOptions WithLimit(int limit)
    {
        Limit = limit;
        return this;
    }

    public SoundOptions WithEnabled(bool enabled)
    {
        Enabled = enabled;
        return this;
    }

    public SoundOptions UseClock(TimeProvider clock)
    {
        _clock = clock;
        return this;
    }

    public override string ToString() => _clock is null ? "no clock" : "clock";
}

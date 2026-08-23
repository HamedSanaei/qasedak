using Qasedak.BuildingBlocks.Application;

namespace Qasedak.Modules.Instagram.UnitTests.TestSupport;

/// <summary>Deterministic clock for reproducible time-dependent behavior.</summary>
public sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
}

using Qasedak.BuildingBlocks.Application;

namespace Qasedak.Modules.Identity.UnitTests.TestSupport;

/// <summary>Deterministic clock for reproducible time-dependent behavior.</summary>
public sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public static readonly FixedClock Default = new(new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero));

    public DateTimeOffset UtcNow { get; } = utcNow;
}

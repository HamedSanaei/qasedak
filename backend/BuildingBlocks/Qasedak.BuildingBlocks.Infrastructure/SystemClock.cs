using Qasedak.BuildingBlocks.Application;

namespace Qasedak.BuildingBlocks.Infrastructure;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

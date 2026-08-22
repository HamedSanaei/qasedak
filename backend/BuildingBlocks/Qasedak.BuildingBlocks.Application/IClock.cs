namespace Qasedak.BuildingBlocks.Application;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

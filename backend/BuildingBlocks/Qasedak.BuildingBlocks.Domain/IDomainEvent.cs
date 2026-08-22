namespace Qasedak.BuildingBlocks.Domain;

public interface IDomainEvent
{
    DateTimeOffset OccurredAtUtc { get; }
}

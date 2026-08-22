using Qasedak.BuildingBlocks.Domain;
using Xunit;

namespace Qasedak.BuildingBlocks.UnitTests;

public sealed class EntityTests
{
    [Fact]
    public void EntityPreservesAssignedId()
    {
        var id = Guid.CreateVersion7();
        var entity = new TestEntity(id);
        Assert.Equal(id, entity.Id);
    }

    private sealed class TestEntity(Guid id) : Entity<Guid>(id);
}

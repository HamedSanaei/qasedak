using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Contacts.Application;
using Qasedak.Modules.Contacts.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Contacts.IntegrationTests;

/// <summary>
/// The interaction projection over real PostgreSQL: find-or-create, ledger-enforced
/// idempotency (unique event index), recency accumulation and merge-pointer resolution
/// for identity lookups after a contact merge.
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class ContactProjectionPersistenceTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 14, 0, 0, TimeSpan.Zero);

    private ProjectContactInteractionUseCase NewUseCase()
    {
        var options = new DbContextOptionsBuilder<ContactsDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ContactsDbContext.Schema))
            .Options;
        var context = new ContactsDbContext(options);
        return new ProjectContactInteractionUseCase(new EfContactRepository(context), new EfContactInteractionLedger(context));
    }

    private static ContactInteractionProjection Projection(Guid workspaceId, string eventId, string identity) =>
        new(workspaceId, "instagram", identity, null, eventId, "message.received", Now);

    [Fact]
    public async Task ProjectionIsIdempotentPerEventAndAccumulatesAcrossEvents()
    {
        var useCase = NewUseCase();
        var workspaceId = Guid.CreateVersion7();

        var first = await useCase.ExecuteAsync(Projection(workspaceId, "evt-a-1", "ig-proj-1"));
        var replayed = await useCase.ExecuteAsync(Projection(workspaceId, "evt-a-1", "ig-proj-1"));
        var second = await useCase.ExecuteAsync(Projection(workspaceId, "evt-a-2", "ig-proj-1"));

        Assert.False(first.Duplicate);
        Assert.True(first.NewContact);
        Assert.True(replayed.Duplicate);
        Assert.Equal(first.ContactId, replayed.ContactId);
        Assert.False(second.Duplicate);

        // Reload through the same backing store and verify accumulated state.
        var repository = new EfContactRepository(
            new ContactsDbContext(new DbContextOptionsBuilder<ContactsDbContext>()
                .UseNpgsql(fixture.Context.Database.GetConnectionString())
                .Options));
        var contact = await repository.FindByIdentityAsync(workspaceId, "instagram", "ig-proj-1");
        Assert.NotNull(contact);
        Assert.Equal(2, contact!.InteractionCount);
        Assert.Equal(Now, contact.LastSeenAtUtc); // both events at the same instant; max stays put
    }

    [Fact]
    public async Task IdentityLookupResolvesMergePointers()
    {
        var useCase = NewUseCase();
        var workspaceId = Guid.CreateVersion7();
        var primary = await useCase.ExecuteAsync(Projection(workspaceId, "evt-m-1", "ig-merge-p"));
        var secondary = await useCase.ExecuteAsync(Projection(workspaceId, "evt-m-2", "ig-merge-s"));

        // Merge secondary into primary via a dedicated scope.
        var repository = new EfContactRepository(
            new ContactsDbContext(new DbContextOptionsBuilder<ContactsDbContext>()
                .UseNpgsql(fixture.Context.Database.GetConnectionString())
                .Options));
        var p = (await repository.FindByIdAsync(primary.ContactId))!;
        var s = (await repository.FindByIdAsync(secondary.ContactId))!;
        p.Absorb(s, Now.AddMinutes(1));
        await repository.SaveChangesAsync(p);
        await repository.SaveChangesAsync(s);

        // The merged contact's identity still resolves — to the merged row with provenance.
        var byIdentity = await repository.FindByIdentityAsync(workspaceId, "instagram", "ig-merge-s");
        Assert.NotNull(byIdentity);
        Assert.Equal(secondary.ContactId, byIdentity!.Id);
        Assert.Equal(primary.ContactId, byIdentity.MergedIntoId);

        // Further activity on that identity keeps landing on the SAME row (no resurrection).
        var followUp = await useCase.ExecuteAsync(Projection(workspaceId, "evt-m-3", "ig-merge-s"));
        Assert.Equal(secondary.ContactId, followUp.ContactId);
    }
}

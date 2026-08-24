using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Contacts.Application;
using Qasedak.Modules.Contacts.Domain;
using Qasedak.Modules.Contacts.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Contacts.IntegrationTests;

/// <summary>
/// Contact persistence over real PostgreSQL: aggregate round-trip fidelity, the
/// workspace-wide unique identity index (identity ownership backstop), and merge
/// provenance across reloads.
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class ContactPersistenceTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private EfContactRepository NewRepository()
    {
        var options = new DbContextOptionsBuilder<ContactsDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ContactsDbContext.Schema))
            .Options;
        return new EfContactRepository(new ContactsDbContext(options));
    }

    private static Contact NewContact(string suffix, string channel = "instagram") =>
        Contact.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Contact " + suffix, channel, "identity-" + suffix, Now);

    [Fact]
    public async Task RoundTripPreservesAggregateWithIdentitiesAndRecency()
    {
        var repository = NewRepository();
        var contact = NewContact("rt-1");
        contact.LinkIdentity("email", "person@example.com", Now.AddMinutes(1));
        contact.RecordInteraction(Now.AddMinutes(2));
        await repository.SaveChangesAsync(contact);

        var loaded = await repository.FindByIdAsync(contact.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ContactStatus.Active, loaded!.Status);
        Assert.Equal("Contact rt-1", loaded.DisplayName);
        Assert.Equal(1, loaded.InteractionCount);
        Assert.Equal(Now.AddMinutes(2), loaded.LastSeenAtUtc);
        Assert.Equal(2, loaded.Identities.Count);
        // Identity lookup normalizes channel casing and matches on the exact provider id.
        Assert.NotNull(await repository.FindByIdentityAsync(loaded.WorkspaceId, "EMAIL", "person@example.com"));
        Assert.NotNull(await repository.FindByIdentityAsync(loaded.WorkspaceId, "instagram", "identity-rt-1"));
    }

    [Fact]
    public async Task WorkspaceIdentityUniquenessIsEnforcedByPersistence()
    {
        // Fresh scope per operation mirrors request-scoped repositories.
        var first = NewContact("uniq-1");
        await NewRepository().SaveChangesAsync(first);

        // A second contact claiming the same workspace identity violates the unique index.
        var impostor = Contact.Create(
            Guid.CreateVersion7(), first.WorkspaceId, "Impostor", "instagram", "identity-uniq-1", Now);
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => NewRepository().SaveChangesAsync(impostor));

        // A different workspace can own the same provider identity independently.
        var otherWorkspace = Contact.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Other ws", "instagram", "identity-uniq-1", Now);
        await NewRepository().SaveChangesAsync(otherWorkspace);
    }

    [Fact]
    public async Task MergePersistsAbsorptionAndProvenanceAcrossReloads()
    {
        var repository = NewRepository();
        var workspaceId = Guid.CreateVersion7();
        var primary = Contact.Create(Guid.CreateVersion7(), workspaceId, "merge-p", "instagram", "identity-merge-p", Now);
        var secondary = Contact.Create(Guid.CreateVersion7(), workspaceId, "merge-s", "instagram", "identity-merge-s", Now);
        secondary.RecordInteraction(Now.AddMinutes(5));
        await repository.SaveChangesAsync(primary);
        await repository.SaveChangesAsync(secondary);

        primary.Absorb(secondary, Now.AddMinutes(6));
        await repository.SaveChangesAsync(primary);
        await repository.SaveChangesAsync(secondary);

        var reloadedPrimary = await NewRepository().FindByIdAsync(primary.Id);
        var reloadedSecondary = await NewRepository().FindByIdAsync(secondary.Id);
        Assert.NotNull(reloadedPrimary);
        Assert.NotNull(reloadedSecondary);
        // Identities stay on the merged row; lookups must resolve MergedIntoId.
        Assert.Single(reloadedPrimary!.Identities);
        Assert.Equal(1, reloadedPrimary.InteractionCount);
        Assert.Equal(Now.AddMinutes(5), reloadedPrimary.LastSeenAtUtc);
        Assert.Equal(ContactStatus.Merged, reloadedSecondary!.Status);
        Assert.Equal(primary.Id, reloadedSecondary.MergedIntoId);

        var byIdentity = await NewRepository().FindByIdentityAsync(workspaceId, "instagram", "identity-merge-s");
        Assert.NotNull(byIdentity);
        Assert.Equal(secondary.Id, byIdentity!.Id);
        Assert.Equal(primary.Id, byIdentity.MergedIntoId);
    }

    [Fact]
    public async Task WorkspaceListingOrdersByRecency()
    {
        var repository = NewRepository();
        var workspaceId = Guid.CreateVersion7();
        var older = Contact.Create(Guid.CreateVersion7(), workspaceId, "older", "instagram", "i-old", Now);
        var newer = Contact.Create(Guid.CreateVersion7(), workspaceId, "newer", "instagram", "i-new", Now);
        older.RecordInteraction(Now);
        newer.RecordInteraction(Now.AddHours(3));
        await repository.SaveChangesAsync(older);
        await repository.SaveChangesAsync(newer);

        var list = await repository.ListByWorkspaceAsync(workspaceId);
        Assert.Equal(["newer", "older"], list.Select(c => c.DisplayName).ToArray());
    }
}

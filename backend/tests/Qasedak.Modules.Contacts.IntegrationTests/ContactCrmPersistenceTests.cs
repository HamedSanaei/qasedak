using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Contacts.Application;
using Qasedak.Modules.Contacts.Domain;
using Qasedak.Modules.Contacts.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Contacts.IntegrationTests;

/// <summary>
/// Tags/notes persistence and workspace-scoped query semantics over real PostgreSQL:
/// upsert convergence for tags, append-only notes, search/status/tag filtering, paging,
/// and strict workspace scoping.
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class ContactCrmPersistenceTests(PostgreSqlFixture fixture)
{
    private (EfContactRepository Repository, IContactQueries Queries) NewScope()
    {
        var context = new ContactsDbContext(new DbContextOptionsBuilder<ContactsDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString())
            .Options);
        return (new EfContactRepository(context), new EfContactQueries(context));
    }

    [Fact]
    public async Task TagsAndNotesSurviveUpsertCycles()
    {
        var (repository, _) = NewScope();
        var contact = Contact.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Tagged", "instagram", "crm-1", DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(contact);

        // Reload, mutate tags/notes through a fresh scope, save.
        var (repo2, _) = NewScope();
        var loaded = (await repo2.FindByIdAsync(contact.Id))!;
        loaded.AddTag("Hot Lead");
        loaded.AddTag("hot lead"); // duplicate after normalization
        loaded.AddNote("first note", DateTimeOffset.UtcNow.AddMinutes(1));
        await repo2.SaveChangesAsync(loaded);

        // Remove one tag and add another; notes must remain untouched.
        var (repo3, _) = NewScope();
        var reloaded = (await repo3.FindByIdAsync(contact.Id))!;
        Assert.Equal(["hot lead"], reloaded.Tags);
        Assert.Single(reloaded.Notes);
        Assert.True(reloaded.RemoveTag("HOT LEAD"));
        reloaded.AddTag("cold");
        reloaded.AddNote("second note", DateTimeOffset.UtcNow.AddMinutes(2));
        await repo3.SaveChangesAsync(reloaded);

        var final = (await NewScope().Repository.FindByIdAsync(contact.Id))!;
        Assert.Equal(["cold"], final.Tags);
        Assert.Equal(2, final.Notes.Count);
    }

    [Fact]
    public async Task ListAppliesSearchStatusTagFiltersAndPaging()
    {
        var (repository, queries) = NewScope();
        var workspaceId = Guid.CreateVersion7();

        foreach (var (name, identity, tag) in new[] { ("Alpha", "q-alpha", "vip"), ("Alphabet", "q-alphabet", "lead"), ("Zulu", "q-zulu", null as string) })
        {
            var contact = Contact.Create(Guid.CreateVersion7(), workspaceId, name, "instagram", identity, DateTimeOffset.UtcNow.AddDays(-1));
            if (tag is not null)
            {
                contact.AddTag(tag);
            }

            await repository.SaveChangesAsync(contact);
        }

        var archived = Contact.Create(Guid.CreateVersion7(), workspaceId, "Archived One", "instagram", "q-archived", DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(archived);
        var archRow = (await repository.FindByIdAsync(archived.Id))!;
        archRow.Archive(DateTimeOffset.UtcNow);
        await repository.SaveChangesAsync(archRow);

        // Search by name substring.
        var search = await queries.ListAsync(workspaceId, ContactFilter.From("alpha", null, null, 1, 25));
        Assert.Equal(2, search.TotalCount);

        // Status filter.
        var openOnly = await queries.ListAsync(workspaceId, ContactFilter.From(null, "active", null, 1, 25));
        Assert.Equal(3, openOnly.TotalCount);

        // Tag filter.
        var vip = await queries.ListAsync(workspaceId, ContactFilter.From(null, null, "VIP", 1, 25));
        Assert.Single(vip.Items);
        Assert.Equal("Alpha", vip.Items[0].DisplayName);

        // Paging: four contacts at size 3 → 3 + 1.
        var pageOne = await queries.ListAsync(workspaceId, ContactFilter.From(null, null, null, 1, 3));
        var pageTwo = await queries.ListAsync(workspaceId, ContactFilter.From(null, null, null, 2, 3));
        Assert.Equal(4, pageOne.TotalCount);
        Assert.Equal(3, pageOne.Items.Count);
        Assert.Single(pageTwo.Items);
        Assert.DoesNotContain(pageTwo.Items, item => pageOne.Items.Any(p => p.Id == item.Id));

        // Strict workspace scoping: another workspace sees nothing.
        var foreign = await queries.ListAsync(Guid.CreateVersion7(), ContactFilter.From(null, null, null, 1, 25));
        Assert.Equal(0, foreign.TotalCount);
    }

    [Fact]
    public async Task DetailIsWorkspaceScopedAndIncludesNotes()
    {
        var (repository, queries) = NewScope();
        var workspaceId = Guid.CreateVersion7();
        var contact = Contact.Create(Guid.CreateVersion7(), workspaceId, "Detailed", "instagram", "detailed-1", DateTimeOffset.UtcNow);
        contact.AddNote("history preserved", DateTimeOffset.UtcNow.AddMinutes(1));
        await repository.SaveChangesAsync(contact);

        var detail = await queries.GetDetailAsync(workspaceId, contact.Id);
        Assert.NotNull(detail);
        Assert.Equal("Detailed", detail!.DisplayName);
        Assert.Single(detail.Notes);
        Assert.Contains(detail.Identities, i => i.Channel == "instagram" && i.ProviderIdentity == "detailed-1");

        // A different workspace cannot resolve this contact.
        Assert.Null(await queries.GetDetailAsync(Guid.CreateVersion7(), contact.Id));
    }
}

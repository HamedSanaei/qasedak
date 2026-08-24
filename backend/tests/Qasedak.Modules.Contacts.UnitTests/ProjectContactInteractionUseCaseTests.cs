using Qasedak.Modules.Contacts.Application;
using Qasedak.Modules.Contacts.Domain;
using Xunit;

namespace Qasedak.Modules.Contacts.UnitTests;

/// <summary>
/// Interaction projection semantics over fakes: find-or-create, ledger-gated idempotency,
/// placeholder name upgrades and the concurrent-create race path.
/// </summary>
public sealed class ProjectContactInteractionUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeContactRepository : IContactRepository
    {
        public readonly List<Contact> Store = [];
        public int SaveCalls;
        public int MissesBeforeHit;

        public Task<Contact?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Store.FirstOrDefault(c => c.Id == id));

        public async Task<Contact?> FindByIdentityAsync(Guid workspaceId, string channel, string providerIdentity, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            if (MissesBeforeHit > 0)
            {
                MissesBeforeHit--;
                return null;
            }

            return Store.FirstOrDefault(c => c.WorkspaceId == workspaceId && c.Identities.Any(i => i.SameAs(channel, providerIdentity)));
        }

        public Task<IReadOnlyList<Contact>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Contact>>(Store.Where(c => c.WorkspaceId == workspaceId).ToList());

        public async Task SaveChangesAsync(Contact contact, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            await Task.Yield();
            var existing = Store.FindIndex(c => c.Id == contact.Id);
            if (existing >= 0)
            {
                // Rehydrate from stored state so mutations behave like a real reload.
                Store[existing] = Contact.FromState(
                    contact.Id, contact.WorkspaceId, contact.DisplayName, contact.Status,
                    contact.FirstSeenAtUtc, contact.FirstSeenAtUtc, contact.LastSeenAtUtc,
                    contact.InteractionCount, contact.MergedIntoId, [.. contact.Identities]);
                return;
            }

            if (Store.Any(c => c.WorkspaceId == contact.WorkspaceId
                && c.Identities.Any(i => i.Channel == "instagram" && i.ProviderIdentity == contact.Identities[0].ProviderIdentity)))
            {
                throw new InvalidOperationException("23505 duplicate key: identity already owned");
            }

            Store.Add(contact);
        }
    }

    private sealed class FakeLedger : IContactInteractionLedger
    {
        public readonly HashSet<string> Seen = [];

        public Task<bool> TryRecordAsync(ContactInteractionEntry entry, CancellationToken cancellationToken = default) =>
            Task.FromResult(Seen.Add(entry.EventId));
    }

    private static ContactInteractionProjection Projection(Guid workspaceId, string eventId, string identity = "sender-1", string? hint = "Ada L.") =>
        new(workspaceId, "instagram", identity, hint, eventId, "message.received", Now);

    [Fact]
    public async Task FirstProjectionCreatesContactAndCountsInteraction()
    {
        var repository = new FakeContactRepository();
        var useCase = new ProjectContactInteractionUseCase(repository, new FakeLedger());

        var outcome = await useCase.ExecuteAsync(Projection(Guid.CreateVersion7(), "evt-1"));

        Assert.False(outcome.Duplicate);
        Assert.True(outcome.NewContact);
        Assert.Equal("Ada L.", repository.Store.Single().DisplayName);
        Assert.Equal(1, repository.Store.Single().InteractionCount);
    }

    [Fact]
    public async Task ReplayedEventIsDuplicateAndNeverRecounts()
    {
        var workspaceId = Guid.CreateVersion7();
        var repository = new FakeContactRepository();
        var ledger = new FakeLedger();
        var useCase = new ProjectContactInteractionUseCase(repository, ledger);

        await useCase.ExecuteAsync(Projection(workspaceId, "evt-1"));
        var replayed = await useCase.ExecuteAsync(Projection(workspaceId, "evt-1"));

        Assert.True(replayed.Duplicate);
        Assert.Equal(1, repository.Store.Single().InteractionCount); // unchanged by the replay
        Assert.Equal(2, repository.SaveCalls); // create + first count only
    }

    [Fact]
    public async Task SecondEventBumpsRecencyAndCountOnExistingContact()
    {
        var workspaceId = Guid.CreateVersion7();
        var repository = new FakeContactRepository();
        var useCase = new ProjectContactInteractionUseCase(repository, new FakeLedger());
        await useCase.ExecuteAsync(Projection(workspaceId, "evt-1"));
        var contactId = repository.Store.Single().Id;

        await useCase.ExecuteAsync(Projection(workspaceId, "evt-2", hint: null));

        var contact = repository.Store.Single();
        Assert.Equal(contactId, contact.Id);
        Assert.Equal(2, contact.InteractionCount);
    }

    [Fact]
    public async Task PlaceholderNameUpgradesOnceRealAttributionArrives()
    {
        var workspaceId = Guid.CreateVersion7();
        var repository = new FakeContactRepository();
        var useCase = new ProjectContactInteractionUseCase(repository, new FakeLedger());
        await useCase.ExecuteAsync(Projection(workspaceId, "evt-1", hint: null)); // placeholder = provider identity

        await useCase.ExecuteAsync(Projection(workspaceId, "evt-2", hint: "Ada Lovelace"));

        Assert.Equal("Ada Lovelace", repository.Store.Single().DisplayName);
    }

    [Fact]
    public async Task ConcurrentCreateRaceAdoptsTheWinningContact()
    {
        var workspaceId = Guid.CreateVersion7();
        var repository = new FakeContactRepository { MissesBeforeHit = 1 };
        var useCase = new ProjectContactInteractionUseCase(repository, new FakeLedger());

        // The winner is persisted between the loser's lookup and save: the first lookup
        // misses, the save hits the unique index, and the retry adopts the winner.
        repository.Store.Add(Contact.Create(Guid.CreateVersion7(), workspaceId, "Winner", "instagram", "race-1", Now));

        var outcome = await useCase.ExecuteAsync(new ContactInteractionProjection(
            workspaceId, "instagram", "race-1", "Loser?", "evt-race", "message.received", Now));

        Assert.False(outcome.NewContact);
        Assert.False(outcome.Duplicate);
        Assert.Equal("Winner", repository.Store.Single(c => c.Identities.Any(i => i.ProviderIdentity == "race-1")).DisplayName);
    }
}

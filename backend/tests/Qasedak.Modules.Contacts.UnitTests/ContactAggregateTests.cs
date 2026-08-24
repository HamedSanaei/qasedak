using Qasedak.Modules.Contacts.Domain;
using Xunit;

namespace Qasedak.Modules.Contacts.UnitTests;

/// <summary>
/// Contact aggregate invariants: identity ownership, recency accounting, merge
/// absorption/provenance and lifecycle guards. Timestamps are always parameters.
/// </summary>
public sealed class ContactAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static Contact NewContact(string identity = "ig-1", string channel = "instagram", string name = "A Customer", Guid? workspaceId = null) =>
        Contact.Create(Guid.CreateVersion7(), workspaceId ?? Guid.CreateVersion7(), name, channel, identity, Now);

    [Fact]
    public void CreateNormalizesAndSeedsFirstIdentity()
    {
        var contact = Contact.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "  Ada Lovelace  ", " Instagram ", " 178414000 ", Now);

        Assert.Equal("Ada Lovelace", contact.DisplayName);
        Assert.Equal(ContactStatus.Active, contact.Status);
        var identity = Assert.Single(contact.Identities);
        Assert.Equal("instagram", identity.Channel);
        Assert.Equal("178414000", identity.ProviderIdentity);
        Assert.Equal(Now, contact.FirstSeenAtUtc);
        Assert.Equal(Now, contact.LastSeenAtUtc);
        Assert.Equal(0, contact.InteractionCount);
    }

    [Fact]
    public void CreateGuardsRejectInvalidInputs()
    {
        Assert.Throws<ContactsDomainException>(() => Contact.Create(Guid.Empty, Guid.CreateVersion7(), "n", "instagram", "i", Now));
        Assert.Throws<ContactsDomainException>(() => Contact.Create(Guid.CreateVersion7(), Guid.Empty, "n", "instagram", "i", Now));
        Assert.Throws<ContactsDomainException>(() => Contact.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "   ", "instagram", "i", Now));
        Assert.Throws<ContactsDomainException>(() => Contact.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), new string('x', Contact.MaxDisplayNameLength + 1), "instagram", "i", Now));
        Assert.Throws<ContactsDomainException>(() => Contact.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "n", "", "i", Now));
        Assert.Throws<ContactsDomainException>(() => Contact.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "n", "instagram", "  ", Now));
    }

    [Fact]
    public void LinkingIdenticalIdentityIsIdempotentNoOp()
    {
        var contact = NewContact();

        Assert.False(contact.LinkIdentity("INSTAGRAM", " ig-1 ", Now.AddMinutes(1)));
        Assert.Single(contact.Identities);
    }

    [Fact]
    public void LinkingNewIdentityAppends()
    {
        var contact = NewContact();

        Assert.True(contact.LinkIdentity("email", "ada@example.com", Now.AddMinutes(1)));

        Assert.Equal(2, contact.Identities.Count);
        Assert.Contains(contact.Identities, i => i.Channel == "email" && i.ProviderIdentity == "ada@example.com");
    }

    [Fact]
    public void IdentityLimitIsEnforced()
    {
        var contact = NewContact();
        for (var i = 2; i <= Contact.MaxIdentitiesPerContact; i++)
        {
            contact.LinkIdentity("channel-" + i, "identity-" + i, Now);
        }

        Assert.Throws<ContactsDomainException>(() => contact.LinkIdentity("one-more", "overflow", Now));
    }

    [Fact]
    public void RecordInteractionBumpsCountAndKeepsRecencyMonotonic()
    {
        var contact = NewContact();
        contact.RecordInteraction(Now.AddHours(2));
        contact.RecordInteraction(Now.AddHours(1)); // late-arriving event must not regress recency

        Assert.Equal(2, contact.InteractionCount);
        Assert.Equal(Now.AddHours(2), contact.LastSeenAtUtc);
    }

    [Fact]
    public void RenameValidatesAndUpdates()
    {
        var contact = NewContact();

        contact.Rename("  Ada L.  ");
        Assert.Equal("Ada L.", contact.DisplayName);
        Assert.Throws<ContactsDomainException>(() => contact.Rename(""));
    }

    [Fact]
    public void ArchiveLifecycleIsGuarded()
    {
        var contact = NewContact();
        contact.Archive(Now.AddMinutes(1));

        Assert.Equal(ContactStatus.Archived, contact.Status);
        Assert.Throws<ContactsDomainException>(() => contact.Archive(Now.AddMinutes(2)));
        Assert.Throws<ContactsDomainException>(() => contact.RecordInteraction(Now.AddMinutes(3)));
        Assert.Throws<ContactsDomainException>(() => contact.LinkIdentity("email", "x@y.z", Now));
    }

    [Fact]
    public void AbsorbCombinesRecencyAndMarksSecondaryMerged()
    {
        var workspaceId = Guid.CreateVersion7();
        var primary = NewContact(identity: "primary-id", workspaceId: workspaceId);
        primary.RecordInteraction(Now);
        var secondary = Contact.Create(Guid.CreateVersion7(), workspaceId, "S", "instagram", "secondary-id", Now.AddDays(-3));
        secondary.RecordInteraction(Now);
        secondary.LinkIdentity("whatsapp", "wa-9", Now);

        primary.Absorb(secondary, Now.AddMinutes(5));

        Assert.Equal(ContactStatus.Merged, secondary.Status);
        Assert.Equal(primary.Id, secondary.MergedIntoId);
        // Identities stay attached to the merged row (lookups resolve MergedIntoId);
        // interaction totals and recency combine into the primary.
        Assert.Single(primary.Identities);
        Assert.Equal(2, secondary.Identities.Count);
        Assert.Equal(2, primary.InteractionCount);
        // Absorption widens recency bounds in both directions.
        Assert.Equal(Now.AddDays(-3), primary.FirstSeenAtUtc);
        Assert.Equal(Now, primary.LastSeenAtUtc);
    }

    [Fact]
    public void AbsorbNoLongerRejectsSharedIdentitiesBecauseOwnershipNeverMoves()
    {
        var workspaceId = Guid.CreateVersion7();
        var primary = NewContact(identity: "primary-id", workspaceId: workspaceId);
        var secondary = NewContact(identity: "primary-id", workspaceId: workspaceId); // impossible via unique index, but harmless in-memory

        primary.Absorb(secondary, Now);
        Assert.Equal(ContactStatus.Merged, secondary.Status);
    }

    [Fact]
    public void AbsorbRejectsNonActiveSecondariesAndCrossWorkspace()
    {
        var primary = NewContact();
        var archivedSecondary = NewContact(identity: "archived-id", workspaceId: primary.WorkspaceId);
        archivedSecondary.Archive(Now);
        Assert.Throws<ContactsDomainException>(() => primary.Absorb(archivedSecondary, Now));

        var foreign = Contact.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Foreign", "instagram", "foreign-id", Now);
        Assert.Throws<ContactsDomainException>(() => primary.Absorb(foreign, Now));
        Assert.Throws<ArgumentNullException>(() => primary.Absorb(null!, Now));
    }

    [Fact]
    public void MergedContactsAreTerminal()
    {
        var workspaceId = Guid.CreateVersion7();
        var primary = NewContact(identity: "p", workspaceId: workspaceId);
        var secondary = NewContact(identity: "s", workspaceId: workspaceId);
        primary.Absorb(secondary, Now);

        // The absorbed contact can never act again.
        Assert.Throws<ContactsDomainException>(() => secondary.Absorb(NewContact(identity: "third"), Now));
        // The primary remains fully active.
        Assert.Equal(ContactStatus.Active, primary.Status);
        primary.Rename("Primary renamed");
        Assert.Equal("Primary renamed", primary.DisplayName);
    }

    [Fact]
    public void FromStateRestoresFullAggregate()
    {
        var identities = new List<SocialIdentity> { new(Guid.CreateVersion7(), Guid.CreateVersion7(), "instagram", "restored", Now) };
        var restored = Contact.FromState(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "Restored", ContactStatus.Archived,
            Now.AddDays(-1), Now.AddDays(-1), Now, 42, null, identities);

        Assert.Equal("Restored", restored.DisplayName);
        Assert.Equal(42, restored.InteractionCount);
        Assert.Equal(ContactStatus.Archived, restored.Status);
        Assert.Single(restored.Identities);
    }
}

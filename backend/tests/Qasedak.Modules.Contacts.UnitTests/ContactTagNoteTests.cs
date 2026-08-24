using Qasedak.Modules.Contacts.Domain;
using Xunit;

namespace Qasedak.Modules.Contacts.UnitTests;

/// <summary>Tag normalization/caps and append-only note semantics.</summary>
public sealed class ContactTagNoteTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TagsNormalizeLowercaseAndDedupeIdempotently()
    {
        var contact = NewContact();

        Assert.True(contact.AddTag("  VIP  "));
        Assert.False(contact.AddTag("vip"));
        Assert.True(contact.Tags.SequenceEqual(["vip"]));
    }

    [Fact]
    public void TagCapsAndLengthGuards()
    {
        var contact = NewContact();
        for (var i = 1; i <= Contact.MaxTagsPerContact; i++)
        {
            contact.AddTag("tag-" + i);
        }

        Assert.Throws<ContactsDomainException>(() => contact.AddTag("one-too-many"));
        Assert.Throws<ContactsDomainException>(() => contact.AddTag(new string('x', Contact.MaxTagLength + 1)));
    }

    [Fact]
    public void RemoveTagIsIdempotentAndNormalizing()
    {
        var contact = NewContact();
        contact.AddTag("lead");

        Assert.True(contact.RemoveTag(" LEAD "));
        Assert.Empty(contact.Tags);
        Assert.False(contact.RemoveTag("lead"));
    }

    [Fact]
    public void NotesAppendOnlyWithGuards()
    {
        var contact = NewContact();
        var note = contact.AddNote("  Called the customer back.  ", Now);
        var second = contact.AddNote("Sent a follow-up.", Now.AddMinutes(5));

        Assert.Equal(2, contact.Notes.Count);
        Assert.Equal("Called the customer back.", note.Body);
        Assert.Equal(Now, note.CreatedAtUtc);
        Assert.NotEqual(note.Id, second.Id);

        // Notes are immutable records: no edit or remove API exists on the aggregate.
        Assert.Throws<ContactsDomainException>(() => contact.AddNote("", Now));
        Assert.Throws<ContactsDomainException>(() => contact.AddNote("   ", Now));
        Assert.Throws<ContactsDomainException>(() => contact.AddNote(new string('x', Contact.MaxNoteLength + 1), Now));
    }

    [Fact]
    public void FromStateRestoresTagsAndNotes()
    {
        var contact = NewContact();
        contact.AddTag("hot");
        contact.AddNote("note body", Now);
        var restored = Contact.FromState(
            contact.Id, contact.WorkspaceId, contact.DisplayName, contact.Status,
            contact.CreatedAtUtc, contact.FirstSeenAtUtc, contact.LastSeenAtUtc,
            contact.InteractionCount, null,
            [.. contact.Identities], [.. contact.Tags], [.. contact.Notes]);

        Assert.Single(restored.Tags);
        Assert.Single(restored.Notes);
        Assert.Equal("note body", restored.Notes[0].Body);
    }

    private static Contact NewContact() =>
        Contact.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Ada", "instagram", "ada-1", Now);
}

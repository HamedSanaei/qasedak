using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Contacts.Application;
using Qasedak.Modules.Contacts.Domain;

namespace Qasedak.Modules.Contacts.Infrastructure.Persistence;

/// <summary>Loads/saves contacts with identities, tags and notes via aggregate upserts.</summary>
public sealed class EfContactRepository(ContactsDbContext context) : IContactRepository
{
    public async Task<Contact?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Contacts
            .Include(c => c.Identities)
            .Include(c => c.Tags)
            .Include(c => c.Notes)
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        return row is null ? null : FromRow(row);
    }

    public async Task<Contact?> FindByIdentityAsync(Guid workspaceId, string channel, string providerIdentity, CancellationToken cancellationToken = default)
    {
        var normalizedChannel = channel.Trim().ToLowerInvariant();
        var row = await context.ContactIdentities
            .AsNoTracking()
            .Where(i => i.WorkspaceId == workspaceId && i.Channel == normalizedChannel && i.ProviderIdentity == providerIdentity.Trim())
            .Select(i => (Guid?)i.ContactId)
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : await FindByIdAsync(row.Value, cancellationToken);
    }

    public async Task<IReadOnlyList<Contact>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var rows = await context.Contacts
            .Include(c => c.Identities)
            .Where(c => c.WorkspaceId == workspaceId)
            .OrderByDescending(c => c.LastSeenAtUtc)
            .ToListAsync(cancellationToken);
        return rows.Select(FromRow).ToList();
    }

    public async Task SaveChangesAsync(Contact contact, CancellationToken cancellationToken = default)
    {
        // Upsert semantics: locals first (inserts within this scope), then the database.
        var row = context.Contacts.Local.FirstOrDefault(r => r.Id == contact.Id)
            ?? await context.Contacts
                .Include(c => c.Identities)
                .FirstOrDefaultAsync(r => r.Id == contact.Id, cancellationToken);

        if (row is null)
        {
            context.Contacts.Add(ToRow(contact));
        }
        else
        {
            // Scalar/merge state is mutable; identities are append-only (merge moves them
            // between aggregates as delete+add on each side).
            row.DisplayName = contact.DisplayName;
            row.Status = contact.Status;
            row.FirstSeenAtUtc = contact.FirstSeenAtUtc;
            row.LastSeenAtUtc = contact.LastSeenAtUtc;
            row.InteractionCount = contact.InteractionCount;
            row.MergedIntoId = contact.MergedIntoId;

            foreach (var identity in contact.Identities)
            {
                if (row.Identities.Any(r => r.Channel == identity.Channel && r.ProviderIdentity == identity.ProviderIdentity))
                {
                    continue;
                }

                row.Identities.Add(new ContactIdentityRow
                {
                    ContactId = contact.Id,
                    WorkspaceId = contact.WorkspaceId,
                    Channel = identity.Channel,
                    ProviderIdentity = identity.ProviderIdentity,
                    LinkedAtUtc = identity.LinkedAtUtc,
                });
            }

            foreach (var tracked in row.Identities
                .Where(r => !contact.Identities.Any(mine => mine.SameAs(r.Channel, r.ProviderIdentity)))
                .ToList())
            {
                row.Identities.Remove(tracked);
            }

            // Tags: converge to the aggregate's set (normalized lowercase).
            var trackedTags = await context.ContactTags
                .Where(t => t.ContactId == contact.Id)
                .ToListAsync(cancellationToken);
            foreach (var tag in trackedTags.Where(t => !contact.Tags.Contains(t.Tag)).ToList())
            {
                context.ContactTags.Remove(tag);
            }

            foreach (var tag in contact.Tags.Where(t => trackedTags.All(existing => existing.Tag != t)))
            {
                context.ContactTags.Add(new ContactTagRow { ContactId = contact.Id, WorkspaceId = contact.WorkspaceId, Tag = tag });
            }

            // Notes are append-only: add any the store has not seen yet, never remove.
            var knownNoteIds = await context.ContactNotes
                .Where(n => n.ContactId == contact.Id)
                .Select(n => n.Id)
                .ToListAsync(cancellationToken);
            foreach (var note in contact.Notes.Where(n => !knownNoteIds.Contains(n.Id)))
            {
                context.ContactNotes.Add(new ContactNoteRow
                {
                    Id = note.Id,
                    ContactId = note.ContactId,
                    WorkspaceId = note.WorkspaceId,
                    Body = note.Body,
                    CreatedAtUtc = note.CreatedAtUtc,
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static Contact FromRow(ContactRow row) => Contact.FromState(
        row.Id,
        row.WorkspaceId,
        row.DisplayName,
        row.Status,
        row.CreatedAtUtc,
        row.FirstSeenAtUtc,
        row.LastSeenAtUtc,
        row.InteractionCount,
        row.MergedIntoId,
        row.Identities
            .Select(i => new SocialIdentity(row.Id, row.WorkspaceId, i.Channel, i.ProviderIdentity, i.LinkedAtUtc))
            .ToList(),
        [.. row.Tags.Select(t => t.Tag)],
        [.. row.Notes.OrderBy(n => n.CreatedAtUtc).Select(n => new ContactNote(n.Id, n.ContactId, n.WorkspaceId, n.Body, n.CreatedAtUtc))]);

    private static ContactRow ToRow(Contact contact) => new()
    {
        Id = contact.Id,
        WorkspaceId = contact.WorkspaceId,
        DisplayName = contact.DisplayName,
        Status = contact.Status,
        CreatedAtUtc = contact.CreatedAtUtc,
        FirstSeenAtUtc = contact.FirstSeenAtUtc,
        LastSeenAtUtc = contact.LastSeenAtUtc,
        InteractionCount = contact.InteractionCount,
        MergedIntoId = contact.MergedIntoId,
        Identities = contact.Identities
            .Select(i => new ContactIdentityRow
            {
                ContactId = contact.Id,
                WorkspaceId = contact.WorkspaceId,
                Channel = i.Channel,
                ProviderIdentity = i.ProviderIdentity,
                LinkedAtUtc = i.LinkedAtUtc,
            })
            .ToList(),
        Tags = contact.Tags.Select(t => new ContactTagRow { ContactId = contact.Id, WorkspaceId = contact.WorkspaceId, Tag = t }).ToList(),
        Notes = contact.Notes.Select(n => new ContactNoteRow
        {
            Id = n.Id,
            ContactId = n.ContactId,
            WorkspaceId = n.WorkspaceId,
            Body = n.Body,
            CreatedAtUtc = n.CreatedAtUtc,
        }).ToList(),
    };
}

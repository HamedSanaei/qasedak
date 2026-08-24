using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Contacts.Application;
using Qasedak.Modules.Contacts.Domain;

namespace Qasedak.Modules.Contacts.Infrastructure.Persistence;

/// <summary>
/// Workspace-scoped contact read model: paged list with search/status/tag filters and
/// full detail with notes. Everything filters on the route workspace first — data from
/// other workspaces can never leak into a result.
/// </summary>
public sealed class EfContactQueries(ContactsDbContext context) : IContactQueries
{
    public async Task<ContactPage> ListAsync(Guid workspaceId, ContactFilter filter, CancellationToken cancellationToken = default)
    {
        var contacts = context.Contacts.AsNoTracking().Where(c => c.WorkspaceId == workspaceId);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            contacts = contacts.Where(c => EF.Functions.ILike(c.DisplayName, "%" + search + "%"));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status)
            && Enum.TryParse<ContactStatus>(filter.Status.Trim(), ignoreCase: true, out var status))
        {
            contacts = contacts.Where(c => c.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Tag))
        {
            var tag = filter.Tag.Trim().ToLowerInvariant();
            contacts = contacts.Where(c => context.ContactTags.Any(t => t.ContactId == c.Id && t.Tag == tag));
        }

        var total = await contacts.CountAsync(cancellationToken);
        var items = await contacts
            .OrderByDescending(c => c.LastSeenAtUtc)
            .Skip(filter.Skip)
            .Take(filter.Take)
            .Select(c => new
            {
                c.Id,
                c.DisplayName,
                Status = c.Status.ToString(),
                c.LastSeenAtUtc,
                c.InteractionCount,
                Tags = context.ContactTags
                    .Where(t => t.ContactId == c.Id)
                    .OrderBy(t => t.Tag)
                    .Select(t => t.Tag)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return new ContactPage(
            items.Select(row => new ContactListRow(row.Id, row.DisplayName, row.Status.ToLowerInvariant(), row.LastSeenAtUtc, row.InteractionCount, row.Tags)).ToList(),
            filter.Page,
            filter.Take,
            total);
    }

    public async Task<ContactDetailRow?> GetDetailAsync(Guid workspaceId, Guid contactId, CancellationToken cancellationToken = default)
    {
        var row = await context.Contacts.AsNoTracking()
            .Include(c => c.Identities)
            .SingleOrDefaultAsync(c => c.WorkspaceId == workspaceId && c.Id == contactId, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var tags = await context.ContactTags.AsNoTracking()
            .Where(t => t.ContactId == contactId)
            .OrderBy(t => t.Tag)
            .Select(t => t.Tag)
            .ToListAsync(cancellationToken);
        var notes = await context.ContactNotes.AsNoTracking()
            .Where(n => n.ContactId == contactId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return new ContactDetailRow(
            row.Id,
            row.DisplayName,
            row.Status.ToString().ToLowerInvariant(),
            row.CreatedAtUtc,
            row.LastSeenAtUtc,
            row.InteractionCount,
            row.MergedIntoId,
            row.Identities.Select(i => (i.Channel, i.ProviderIdentity)).ToList(),
            tags,
            notes.Select(n => (n.Id, n.Body, n.CreatedAtUtc)).ToList());
    }
}

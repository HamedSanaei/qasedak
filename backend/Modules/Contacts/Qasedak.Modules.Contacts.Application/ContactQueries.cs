namespace Qasedak.Modules.Contacts.Application;

/// <summary>Contact list row: CRM projection without loading note bodies.</summary>
public sealed record ContactListRow(
    Guid Id,
    string DisplayName,
    string Status,
    DateTimeOffset LastSeenAtUtc,
    long InteractionCount,
    IReadOnlyList<string> Tags);

/// <summary>Contact detail row with the full note history.</summary>
public sealed record ContactDetailRow(
    Guid Id,
    string DisplayName,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    long InteractionCount,
    Guid? MergedIntoId,
    IReadOnlyList<(string Channel, string ProviderIdentity)> Identities,
    IReadOnlyList<string> Tags,
    IReadOnlyList<(Guid Id, string Body, DateTimeOffset CreatedAtUtc)> Notes);

public sealed record ContactPage(IReadOnlyList<ContactListRow> Items, int Page, int PageSize, int TotalCount);

/// <summary>List filter: free-text name search, status and tag filters, paging.</summary>
public sealed record ContactFilter(string? Search, string? Status, string? Tag, int Page, int PageSize)
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 25;

    public int Skip => Math.Max(0, Page - 1) * Take;

    public int Take => Math.Clamp(PageSize, 1, MaxPageSize);

    public static ContactFilter From(string? search, string? status, string? tag, int page, int pageSize) =>
        new(search, status, tag, page < 1 ? 1 : page, pageSize < 1 ? DefaultPageSize : pageSize);
}

/// <summary>Read-side boundary for workspace contact queries (no aggregate loading).</summary>
public interface IContactQueries
{
    Task<ContactPage> ListAsync(Guid workspaceId, ContactFilter filter, CancellationToken cancellationToken = default);

    /// <summary>Returns the contact detail when it belongs to the workspace; otherwise null.</summary>
    Task<ContactDetailRow?> GetDetailAsync(Guid workspaceId, Guid contactId, CancellationToken cancellationToken = default);
}

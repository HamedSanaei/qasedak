using Qasedak.Modules.Conversations.Domain.Conversations;

namespace Qasedak.Modules.Conversations.Application.Conversations;

/// <summary>Inbox list row: projection without loading message bodies.</summary>
public sealed record InboxConversationRow(
    Guid Id,
    string Channel,
    string ParticipantId,
    string Status,
    DateTimeOffset LastMessageAtUtc,
    int UnreadCount,
    string? LastMessagePreview);

public sealed record InboxMessageRow(
    Guid Id,
    MessageDirection Direction,
    string? ProviderMessageId,
    string SenderId,
    string Body,
    DateTimeOffset OccurredAtUtc);

public sealed record InboxPage(IReadOnlyList<InboxConversationRow> Items, int Page, int PageSize, int TotalCount);

public sealed record InboxFilter(string? Status, int Page, int PageSize)
{
    public int Skip => Math.Max(0, (Page - 1)) * Math.Clamp(PageSize, 1, MaxPageSize);

    public int Take => Math.Clamp(PageSize, 1, MaxPageSize);

    public const int MaxPageSize = 100;

    public static InboxFilter From(string? status, int page, int pageSize) =>
        new(status, page < 1 ? 1 : page, pageSize < 1 ? DefaultPageSize : pageSize);

    public const int DefaultPageSize = 25;
}

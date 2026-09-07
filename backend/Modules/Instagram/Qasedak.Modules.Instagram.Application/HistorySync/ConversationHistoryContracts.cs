namespace Qasedak.Modules.Instagram.Application.HistorySync;

/// <summary>
/// One provider conversation row from the verified Instagram Login Conversations API
/// (retrieved 2026-09-07): <c>GET {graph}/{version}/{IG_ID}/conversations?platform=instagram</c>
/// with <c>instagram_business_basic</c> + <c>instagram_business_manage_messages</c>,
/// Bearer IG User token, host <c>graph.instagram.com</c>. Conversations in the Requests
/// folder that have not been active for 30 days are NOT returned by the provider — a
/// documented coverage limitation, never recoverable through this surface.
/// </summary>
public sealed record ProviderConversationRow(
    string ConversationId,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>One mapped conversations page; cursor is the bounded opaque provider
/// cursor component (never a URL); null means no further pages.</summary>
public sealed record ProviderConversationPage(
    IReadOnlyList<ProviderConversationRow> Conversations,
    string? NextAfterCursor,
    bool HasMore);

/// <summary>
/// One message-id row from <c>GET /{CONVERSATION_ID}?fields=messages</c>. The provider
/// exposes ALL message ids in the conversation but detail is only retrievable for the
/// 20 most recent — this row never implies detail availability.
/// </summary>
public sealed record ProviderConversationMessageRow(
    string MessageId,
    DateTimeOffset? CreatedAtUtc,
    bool IsUnsupported);

/// <summary>
/// One message detail from <c>GET /{MESSAGE_ID}?fields=id,created_time,from,to,message</c>.
/// Provider limitations honored: shares expose a URL only; missing text never means an
/// empty text message; <c>is_unsupported</c> is preserved truthfully.
/// </summary>
public sealed record ProviderMessageDetailRow(
    string MessageId,
    DateTimeOffset? CreatedAtUtc,
    string? FromId,
    IReadOnlyList<string> ToIds,
    string? Body,
    bool IsUnsupported,
    string? ShareUrl);

/// <summary>Outcome of one conversation-list page call; never throws for provider behavior.</summary>
public abstract record ConversationListResult
{
    public sealed record Ok(ProviderConversationPage Page) : ConversationListResult;

    public sealed record Failed(string FailureCode, bool Transient) : ConversationListResult;
}

/// <summary>Outcome of one conversation-messages call; never throws for provider behavior.</summary>
public abstract record ConversationMessagesResult
{
    public sealed record Ok(IReadOnlyList<ProviderConversationMessageRow> Messages) : ConversationMessagesResult;

    public sealed record Failed(string FailureCode, bool Transient) : ConversationMessagesResult;
}

/// <summary>Outcome of one message-detail call; never throws for provider behavior.</summary>
public abstract record MessageDetailResult
{
    public sealed record Ok(ProviderMessageDetailRow Detail) : MessageDetailResult;

    public sealed record Failed(string FailureCode, bool Transient) : MessageDetailResult;
}

/// <summary>
/// Focused provider-facing port for bounded Instagram conversation history (M13-013
/// Phase B). Never a giant messaging client: history-only operations, transport-free
/// Qasedak-owned rows, no raw Graph DTOs/paging URLs/tokens. The caller resolves
/// exact-account authorization and the protected token.
/// </summary>
public interface IInstagramConversationHistoryClient
{
    Task<ConversationListResult> ListConversationsPageAsync(
        string accessToken,
        string providerAccountId,
        int limit,
        string? afterCursor,
        CancellationToken cancellationToken = default);

    Task<ConversationMessagesResult> GetConversationMessagesPageAsync(
        string accessToken,
        string conversationId,
        CancellationToken cancellationToken = default);

    Task<MessageDetailResult> GetMessageDetailAsync(
        string accessToken,
        string messageId,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable failure codes for conversation-history reads.</summary>
public static class ConversationHistoryFailures
{
    public const string Unavailable = "conversations.unavailable";

    public const string RateLimited = "conversations.rateLimited";

    public const string PermissionLoss = "conversations.permissionLoss";

    public const string Authentication = "conversations.authentication";

    public const string NotFound = "conversations.notFound";

    public const string Malformed = "conversations.malformed";

    public const string CursorOversized = "conversations.cursorOversized";

    public const string CursorLoop = "conversations.cursorLoop";

    /// <summary>
    /// The provider withholds detail for messages older than the 20 most recent
    /// (documented "deleted" error shape). NOT a deletion signal and NOT retryable —
    /// the message exists, its detail is simply outside the provider's history window.
    /// </summary>
    public const string HistoryUnavailableOutsideRecentWindow = "conversations.historyUnavailableOutsideRecentWindow";
}

/// <summary>Channel-neutral direction of one imported history message.</summary>
public enum HistoryMessageDirection
{
    Inbound = 1,
    Outbound = 2,
}

/// <summary>Channel-neutral content classification for one imported history message.</summary>
public enum HistoryContentKind
{
    /// <summary>Provider supplied plain text.</summary>
    Text = 1,

    /// <summary>Provider marked the message unsupported (is_unsupported=true).</summary>
    Unsupported = 2,

    /// <summary>Share content: provider exposes a URL only.</summary>
    Share = 3,

    /// <summary>Supported message without text/share content (e.g. media-only). Never
    /// fabricated as an empty text message.</summary>
    NoText = 4,
}

/// <summary>
/// One normalized history message ready to cross the module boundary (Instagram-owned
/// shape). The composition root maps this into the Conversations module's channel-neutral
/// import contract — Instagram never writes the conversations schema.
/// </summary>
public sealed record HistoryMessageImport(
    Guid WorkspaceId,
    Guid ConnectedAccountId,
    string ParticipantId,
    string ProviderMessageId,
    HistoryMessageDirection Direction,
    string SenderId,
    string? Body,
    DateTimeOffset OccurredAtUtc,
    HistoryContentKind ContentKind);

using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.HistorySync;
using Qasedak.Modules.Instagram.Infrastructure.Graph;

namespace Qasedak.Modules.Instagram.Infrastructure.HistorySync;

/// <summary>
/// M13-013 Phase B adapter over the shared M13-003 transport for the verified
/// Instagram Login Conversations API (retrieved 2026-09-07):
/// <list type=\"bullet\">
/// <item><c>GET {graph}/{version}/{IG_ID}/conversations?platform=instagram</c> — conversation ids + updated_time, cursor-paged;</item>
/// <item><c>GET {graph}/{version}/{CONVERSATION_ID}?fields=messages</c> — message ids + created_time (ALL ids, details only for newest 20);</item>
/// <item><c>GET {graph}/{version}/{MESSAGE_ID}?fields=id,created_time,from,to,message</c> — message detail.</item>
/// </list>
/// Permissions <c>instagram_business_basic</c> + <c>instagram_business_manage_messages</c>,
/// Bearer IG User token, host <c>graph.instagram.com</c>. Cursor security: only the
/// bounded opaque cursor component is extracted from paging and rebuilt into known
/// versioned URIs — provider <c>paging.next</c> URLs are never followed (§42). The
/// provider's older-than-20 detail failure is classified
/// <see cref="ConversationHistoryFailures.HistoryUnavailableOutsideRecentWindow"/>
/// (non-retryable, never a deletion signal). Raw Graph DTOs and token material never
/// escape.
/// </summary>
public sealed class GraphInstagramConversationHistoryClient(
    HttpClient http,
    IOptions<MetaGraphOptions> graphOptions) : IInstagramConversationHistoryClient
{
    public const string HttpClientName = "MetaInstagramConversationHistory";

    private readonly MetaGraphTransport _transport = new(http, graphOptions.Value.TimeoutSeconds);

    private readonly MetaGraphOptions _graph = graphOptions.Value;

    public GraphInstagramConversationHistoryClient(HttpClient http)
        : this(http, Microsoft.Extensions.Options.Options.Create(new MetaGraphOptions()))
    {
    }

    public async Task<ConversationListResult> ListConversationsPageAsync(
        string accessToken, string providerAccountId, int limit, string? afterCursor, CancellationToken cancellationToken = default)
    {
        if (afterCursor is not null && afterCursor.Length > ConversationHistoryPolicy.MaxProviderCursorLength)
        {
            return new ConversationListResult.Failed(ConversationHistoryFailures.CursorOversized, Transient: false);
        }

        var query = "platform=instagram"
            + "&limit=" + limit.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + (afterCursor is null ? string.Empty : "&after=" + Uri.EscapeDataString(afterCursor));

        var endpoint = MetaGraphUris.Versioned(_graph.GraphHost, _graph.ApiVersion, $"{providerAccountId}/conversations", query).ToString();
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var outcome = await _transport.SendAsync(request, cancellationToken);
        return outcome switch
        {
            MetaGraphCallResult.Success success => InterpretConversationList(success.Document),
            MetaGraphCallResult.Rejected rejected => MapFailure(rejected.Error),
            _ => new ConversationListResult.Failed(ConversationHistoryFailures.Unavailable, Transient: true),
        };
    }

    public async Task<ConversationMessagesResult> GetConversationMessagesPageAsync(
        string accessToken, string conversationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId) || conversationId.Length > 128)
        {
            return new ConversationMessagesResult.Failed(ConversationHistoryFailures.Malformed, Transient: false);
        }

        var endpoint = MetaGraphUris.Versioned(_graph.GraphHost, _graph.ApiVersion, conversationId, "fields=messages").ToString();
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var outcome = await _transport.SendAsync(request, cancellationToken);
        return outcome switch
        {
            MetaGraphCallResult.Success success => InterpretConversationMessages(success.Document),
            MetaGraphCallResult.Rejected rejected => MapMessagesFailure(rejected.Error),
            _ => new ConversationMessagesResult.Failed(ConversationHistoryFailures.Unavailable, Transient: true),
        };
    }

    public async Task<MessageDetailResult> GetMessageDetailAsync(
        string accessToken, string messageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId) || messageId.Length > 128)
        {
            return new MessageDetailResult.Failed(ConversationHistoryFailures.Malformed, Transient: false);
        }

        var endpoint = MetaGraphUris.Versioned(_graph.GraphHost, _graph.ApiVersion, messageId, "fields=id,created_time,from,to,message,is_unsupported").ToString();
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var outcome = await _transport.SendAsync(request, cancellationToken);
        return outcome switch
        {
            MetaGraphCallResult.Success success => InterpretMessageDetail(success.Document),
            MetaGraphCallResult.Rejected rejected => MapDetailFailure(rejected.Error),
            _ => new MessageDetailResult.Failed(ConversationHistoryFailures.Unavailable, Transient: true),
        };
    }

    private static ConversationListResult InterpretConversationList(JsonDocument document)
    {
        try
        {
            var root = document.RootElement;
            var rows = new List<ProviderConversationRow>();
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(id.GetString()))
                    {
                        continue;
                    }

                    DateTimeOffset? updated = null;
                    if (item.TryGetProperty("updated_time", out var time))
                    {
                        updated = ParseUnixSeconds(time);
                    }

                    rows.Add(new ProviderConversationRow(id.GetString()!, updated));
                }
            }

            var (next, hasMore) = ReadNextCursor(root);
            return new ConversationListResult.Ok(new ProviderConversationPage(rows, next, hasMore));
        }
        catch (JsonException)
        {
            return new ConversationListResult.Failed(ConversationHistoryFailures.Malformed, Transient: false);
        }
    }

    private static ConversationMessagesResult InterpretConversationMessages(JsonDocument document)
    {
        try
        {
            var root = document.RootElement;
            var rows = new List<ProviderConversationMessageRow>();
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Object &&
                messages.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(id.GetString()))
                    {
                        continue;
                    }

                    DateTimeOffset? created = null;
                    if (item.TryGetProperty("created_time", out var time))
                    {
                        created = ParseUnixSeconds(time);
                    }

                    var unsupported = item.TryGetProperty("is_unsupported", out var flag) && flag.ValueKind == JsonValueKind.True;
                    rows.Add(new ProviderConversationMessageRow(id.GetString()!, created, unsupported));
                }
            }

            return new ConversationMessagesResult.Ok(rows);
        }
        catch (JsonException)
        {
            return new ConversationMessagesResult.Failed(ConversationHistoryFailures.Malformed, Transient: false);
        }
    }

    private static MessageDetailResult InterpretMessageDetail(JsonDocument document)
    {
        try
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(id.GetString()))
            {
                return new MessageDetailResult.Failed(ConversationHistoryFailures.Malformed, Transient: false);
            }

            DateTimeOffset? created = null;
            if (root.TryGetProperty("created_time", out var time))
            {
                created = ParseUnixSeconds(time);
            }

            var fromId = ReadNestedId(root, "from");
            var toIds = new List<string>();
            if (root.TryGetProperty("to", out var to) && to.ValueKind == JsonValueKind.Object &&
                to.TryGetProperty("data", out var toData) && toData.ValueKind == JsonValueKind.Array)
            {
                foreach (var recipient in toData.EnumerateArray())
                {
                    if (recipient.TryGetProperty("id", out var recipientId) && recipientId.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(recipientId.GetString()))
                    {
                        toIds.Add(recipientId.GetString()!);
                    }
                }
            }

            var body = ReadString(root, "message");
            var unsupported = root.TryGetProperty("is_unsupported", out var flag) && flag.ValueKind == JsonValueKind.True;

            // Shares expose a URL only (provider limitation). The share envelope may be
            // {share:{media_type, link}} or {attachments...}; we surface only a bounded
            // URL — never invent content.
            string? shareUrl = null;
            if (root.TryGetProperty("share", out var share) && share.ValueKind == JsonValueKind.Object)
            {
                shareUrl = ReadString(share, "link") ?? ReadString(share, "url");
                if (shareUrl is not null && shareUrl.Length > ConversationHistoryPolicy.MaxShareUrlLength)
                {
                    shareUrl = shareUrl[..ConversationHistoryPolicy.MaxShareUrlLength];
                }
            }

            return new MessageDetailResult.Ok(new ProviderMessageDetailRow(
                id.GetString()!, created, fromId, toIds, body, unsupported, shareUrl));
        }
        catch (JsonException)
        {
            return new MessageDetailResult.Failed(ConversationHistoryFailures.Malformed, Transient: false);
        }
    }

    /// <summary>Extracts ONLY the bounded opaque cursor component; never follows URLs.</summary>
    private static (string? NextCursor, bool HasMore) ReadNextCursor(JsonElement root)
    {
        if (!root.TryGetProperty("paging", out var paging) || paging.ValueKind != JsonValueKind.Object)
        {
            return (null, false);
        }

        var hasMore = paging.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(next.GetString());
        if (!paging.TryGetProperty("cursors", out var cursors) || cursors.ValueKind != JsonValueKind.Object ||
            !cursors.TryGetProperty("after", out var after) || after.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(after.GetString()))
        {
            return (null, hasMore);
        }

        return (after.GetString(), hasMore);
    }

    private static DateTimeOffset? ParseUnixSeconds(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var seconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }

        if (element.ValueKind == JsonValueKind.String &&
            long.TryParse(element.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            return DateTimeOffset.FromUnixTimeSeconds(parsed);
        }

        return null;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadNestedId(JsonElement element, string property) =>
        element.TryGetProperty(property, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? ReadString(nested, "id")
            : null;

    private static ConversationListResult.Failed MapFailure(MetaGraphError error) =>
        Classify(error) switch
        {
            MetaGraphFailure.RateLimited => new ConversationListResult.Failed(ConversationHistoryFailures.RateLimited, Transient: true),
            MetaGraphFailure.PermissionLoss => new ConversationListResult.Failed(ConversationHistoryFailures.PermissionLoss, Transient: false),
            MetaGraphFailure.AuthenticationInvalid or MetaGraphFailure.TokenExpired or MetaGraphFailure.Revoked =>
                new ConversationListResult.Failed(ConversationHistoryFailures.Authentication, Transient: false),
            MetaGraphFailure.NotFound => new ConversationListResult.Failed(ConversationHistoryFailures.NotFound, Transient: false),
            MetaGraphFailure.Transient or MetaGraphFailure.TransportFailure =>
                new ConversationListResult.Failed(ConversationHistoryFailures.Unavailable, Transient: true),
            _ => new ConversationListResult.Failed(ConversationHistoryFailures.Malformed, Transient: false),
        };

    private static ConversationMessagesResult.Failed MapMessagesFailure(MetaGraphError error) =>
        Classify(error) switch
        {
            MetaGraphFailure.RateLimited => new ConversationMessagesResult.Failed(ConversationHistoryFailures.RateLimited, Transient: true),
            MetaGraphFailure.PermissionLoss => new ConversationMessagesResult.Failed(ConversationHistoryFailures.PermissionLoss, Transient: false),
            MetaGraphFailure.AuthenticationInvalid or MetaGraphFailure.TokenExpired or MetaGraphFailure.Revoked =>
                new ConversationMessagesResult.Failed(ConversationHistoryFailures.Authentication, Transient: false),
            MetaGraphFailure.NotFound => new ConversationMessagesResult.Failed(ConversationHistoryFailures.NotFound, Transient: false),
            MetaGraphFailure.Transient or MetaGraphFailure.TransportFailure =>
                new ConversationMessagesResult.Failed(ConversationHistoryFailures.Unavailable, Transient: true),
            _ => new ConversationMessagesResult.Failed(ConversationHistoryFailures.Malformed, Transient: false),
        };

    private static MessageDetailResult.Failed MapDetailFailure(MetaGraphError error)
    {
        var failure = MetaGraphClassifier.Classify(error);
        return failure switch
        {
            MetaGraphFailure.RateLimited => new MessageDetailResult.Failed(ConversationHistoryFailures.RateLimited, Transient: true),
            MetaGraphFailure.PermissionLoss => new MessageDetailResult.Failed(ConversationHistoryFailures.PermissionLoss, Transient: false),
            MetaGraphFailure.AuthenticationInvalid or MetaGraphFailure.TokenExpired or MetaGraphFailure.Revoked =>
                new MessageDetailResult.Failed(ConversationHistoryFailures.Authentication, Transient: false),
            // The documented "deleted" shape for messages older than the 20-detail
            // window (and any other non-transient provider refusal of an individual
            // detail) is a history limitation: bounded, non-retryable, NEVER a
            // deletion signal and NEVER account-unhealthy (§75/§76/§88).
            MetaGraphFailure.Transient or MetaGraphFailure.TransportFailure =>
                new MessageDetailResult.Failed(ConversationHistoryFailures.Unavailable, Transient: true),
            _ => new MessageDetailResult.Failed(ConversationHistoryFailures.HistoryUnavailableOutsideRecentWindow, Transient: false),
        };
    }

    private static MetaGraphFailure Classify(MetaGraphError error) => MetaGraphClassifier.Classify(error);
}

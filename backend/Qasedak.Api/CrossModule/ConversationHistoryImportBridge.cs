using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Conversations.Application.Conversations;
using Qasedak.Modules.Conversations.Domain.Conversations;
using Qasedak.Modules.Instagram.Application.HistorySync;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Composition-root bridge (M13-013 §50): maps Instagram history rows into the
/// Conversations module's channel-neutral import contract. Instagram Infrastructure
/// never writes the conversations schema; neither module references the other.
/// Import semantics stay non-webhook: no unread inflation, no automation fan-out.
/// </summary>
public sealed class ConversationHistoryImportBridge(ImportProviderHistoryUseCase import) : IConversationHistoryImportGateway
{
    private const string Channel = "instagram";

    public Task<bool> ImportAsync(HistoryMessageImport message, CancellationToken cancellationToken = default)
    {
        var direction = message.Direction == HistoryMessageDirection.Inbound
            ? MessageDirection.Inbound
            : MessageDirection.Outbound;

        var contentKind = message.ContentKind switch
        {
            HistoryContentKind.Unsupported => MessageContentKind.Unsupported,
            HistoryContentKind.Share => MessageContentKind.Share,
            HistoryContentKind.NoText => MessageContentKind.NoText,
            _ => MessageContentKind.Text,
        };

        return import.ExecuteAsync(new ProviderHistoryMessageImport(
            message.WorkspaceId,
            Channel,
            ChannelAccountId.From(message.ConnectedAccountId),
            message.ParticipantId,
            message.ProviderMessageId,
            direction,
            message.SenderId,
            message.Body,
            message.OccurredAtUtc,
            contentKind), cancellationToken);
    }
}

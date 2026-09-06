using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Contacts.Application;
using Qasedak.Modules.Instagram.Application.Webhooks;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Composition-root bridge: normalized Instagram message and comment events maintain the
/// workspace's contacts. Message senders and comment authors are projected as social
/// identities; the interaction ledger makes webhook redelivery and retries
/// at-most-one-interaction. Exact-account resolution happened upstream (M13-008), so the
/// event's Qasedak workspace key is used directly — never a provider-id re-resolution.
/// Comment usernames (webhook-provided display metadata) upgrade placeholder display
/// names. Events without a resolved account never reach this bridge; null keys are logged
/// and skipped defensively.
/// </summary>
public sealed partial class ContactsInteractionBridge(
    ProjectContactInteractionUseCase projection,
    ILogger<ContactsInteractionBridge> logger) : IIntegrationEventDispatcher
{
    private const string Channel = "instagram";

    public async Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        switch (integrationEvent)
        {
            case InstagramMessageReceived message:
                await ProjectAsync(message.WorkspaceId, message.SenderId, null, message.EventId, "message.received", message.SentAtUtc, cancellationToken);
                break;

            case InstagramCommentCreated comment:
                await ProjectAsync(comment.WorkspaceId, comment.FromId, comment.CommenterUsername, comment.EventId, "comment.created", comment.CreatedAtUtc, cancellationToken);
                break;

            default:
                LogSkipped(integrationEvent.EventId, integrationEvent.GetType().Name);
                break;
        }
    }

    private async Task ProjectAsync(Guid? workspaceId, string? participantIdentity, string? displayNameHint, string eventId, string kind, DateTimeOffset occurredAtUtc, CancellationToken cancellationToken)
    {
        if (workspaceId is null || participantIdentity is null)
        {
            LogUnbound(eventId);
            return;
        }

        var outcome = await projection.ExecuteAsync(new ContactInteractionProjection(
            workspaceId.Value,
            Channel,
            participantIdentity,
            displayNameHint,
            eventId,
            kind,
            occurredAtUtc), cancellationToken);

        LogProjected(eventId, kind, outcome.ContactId, outcome.Duplicate);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Contact interaction projected eventId={EventId} kind={Kind} contact={ContactId} duplicate={Duplicate}")]
    private partial void LogProjected(string eventId, string kind, Guid contactId, bool duplicate);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Integration event skipped eventId={EventId} kind={Kind}")]
    private partial void LogSkipped(string eventId, string kind);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Interaction event dropped: no exact connected account on event eventId={EventId}")]
    private partial void LogUnbound(string eventId);
}

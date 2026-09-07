using Qasedak.Modules.Instagram.Application.RevealFlow;
using Qasedak.Modules.Instagram.Application.Webhooks;

namespace Qasedak.Api.CrossModule;

/// <summary>Invocation-owned content request for starting a reveal flow from a comment (M13-011).</summary>
public sealed record RevealFlowStartRequest(RevealFlowContent Content, FollowGateMode FollowGateMode);

/// <summary>
/// Composition-root seam for starting reveal flows from comment events. M13-012 will map
/// stable automation configuration into this; until then the default no-op provider means
/// production starts no flows (M13-011 ships the durable capability + continuations).
/// </summary>
public interface IRevealFlowStartContentProvider
{
    /// <summary>Returns the content/mode for a comment-originated reveal flow, or null to not start one.</summary>
    RevealFlowStartRequest? RequestFor(InstagramCommentCreated comment);
}

/// <summary>Default production provider: reveal flows are not auto-started until M13-012 configuration exists.</summary>
public sealed class NoRevealFlowStartProvider : IRevealFlowStartContentProvider
{
    public RevealFlowStartRequest? RequestFor(InstagramCommentCreated comment) => null;
}

/// <summary>
/// Composition-root start bridge (M13-011): a comment event starts the durable reveal
/// flow when the configured content provider requests one. Content is invocation-owned —
/// it never lands in AutomationDefinition. The M13-009 comment effect ledger arbitrates
/// against any automation that may concurrently send the same opening Private Reply
/// (AlreadyClaimed → adopt stored provider identity, still exactly one opening message).
/// </summary>
public sealed class RevealFlowStartBridge(
    RevealFlowCoordinator coordinator,
    IRevealFlowStartContentProvider contentProvider) : IIntegrationEventDispatcher
{
    public async Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        if (integrationEvent is not InstagramCommentCreated comment
            || comment.ConnectedAccountId is not { } accountId
            || comment.WorkspaceId is not { } workspaceId)
        {
            // Unenriched or not a comment — nothing to start (zero provider traffic).
            return;
        }

        var request = contentProvider.RequestFor(comment);
        if (request is null)
        {
            return;
        }

        await coordinator.StartFromCommentAsync(new StartRevealFlowCommand(
            workspaceId,
            accountId,
            comment.CommentId,
            comment.FromId,
            comment.IsLiveComment,
            comment.CreatedAtUtc,
            request.Content,
            request.FollowGateMode), cancellationToken);
    }
}

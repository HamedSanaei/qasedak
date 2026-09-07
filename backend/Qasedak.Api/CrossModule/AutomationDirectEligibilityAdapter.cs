using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Conversations.Application.Conversations;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Channel-neutral <see cref="IDirectEligibilityPort"/> implementation (M13-012 §49-50):
/// - account eligibility comes from the exact connected account (workspace match, active,
///   Instagram Login path, stored token present) — never another account;
/// - the 24h window anchor is the latest locally-projected INBOUND user-message time for
///   the exact (channel account, participant) from the Conversations projection — never a
///   comment/private-reply/postback/schedule timestamp;
/// - transient projection failures map to Unknown (retryable, zero provider traffic).
/// Meta remains the final window authority at send time.
/// </summary>
public sealed class AutomationDirectEligibilityAdapter(
    IConversationQueries conversations,
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens) : IDirectEligibilityPort
{
    /// <summary>Same boundary as the M13-011 reveal flow's Direct window (24h from the latest inbound message).</summary>
    private static readonly TimeSpan DirectWindow = TimeSpan.FromHours(24);

    public async Task<DirectEligibility> EvaluateAsync(
        Guid workspaceId,
        ChannelAccountId channelAccountId,
        string participantId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default)
    {
        if (!channelAccountId.IsResolved || string.IsNullOrWhiteSpace(participantId))
        {
            return DirectEligibility.RecipientUnavailable;
        }

        try
        {
            var account = await accounts.FindByIdAsync(channelAccountId.Value, cancellationToken);
            if (account is null || account.WorkspaceId != workspaceId || account.IsDisconnected
                || account.Path != ConnectionPath.InstagramLogin)
            {
                return DirectEligibility.AccountUnavailable;
            }

            var accessToken = await tokens.GetAsync(account.Id, cancellationToken);
            if (string.IsNullOrEmpty(accessToken))
            {
                return DirectEligibility.AccountUnavailable;
            }

            var latestInbound = await conversations.GetLatestInboundOccurredAtUtcAsync(
                workspaceId, InstagramReplyGateway.Channel, channelAccountId, participantId, cancellationToken);
            if (latestInbound is null)
            {
                return DirectEligibility.RecipientUnavailable;
            }

            return nowUtc - latestInbound.Value > DirectWindow
                ? DirectEligibility.WindowExpired
                : DirectEligibility.Eligible;
        }
        catch (Exception)
        {
            // Transient projection failure — safe to re-check later; no marker, no call.
            return DirectEligibility.Unknown;
        }
    }
}

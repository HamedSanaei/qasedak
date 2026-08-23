using Microsoft.Extensions.Logging;
using Qasedak.Modules.Conversations.Application.Conversations;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Binds Conversations' channel-neutral gateway to Instagram: resolves the workspace's
/// connected account, decrypts its stored token and sends via the messaging adapter.
/// Failure codes are stable and log-safe; token material never crosses this boundary.
/// </summary>
public sealed partial class InstagramReplyGateway(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    IInstagramMessagingClient messaging,
    ILogger<InstagramReplyGateway> logger) : IConversationChannelGateway
{
    public const string Channel = "instagram";

    public async Task<ChannelDeliveryResult> DeliverAsync(ChannelDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Channel, Channel, StringComparison.OrdinalIgnoreCase))
        {
            return ChannelDeliveryResult.Rejected("channel.unsupported");
        }

        var account = (await accounts.ListByWorkspaceAsync(request.WorkspaceId, cancellationToken))
            .FirstOrDefault(a => a.Path == ConnectionPath.InstagramLogin && a.DisconnectedAtUtc is null);
        if (account is null)
        {
            LogNoAccount(logger);
            return ChannelDeliveryResult.Rejected("instagram.noConnectedAccount");
        }

        var accessToken = await tokens.GetAsync(account.Id, cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            LogTokenMissing(logger);
            return ChannelDeliveryResult.Rejected("instagram.tokenMissing");
        }

        var result = await messaging.SendTextAsync(accessToken, request.ParticipantId, request.Text, cancellationToken);
        return result.Succeeded
            ? ChannelDeliveryResult.Delivered()
            : result.Failure!.Reason switch
            {
                MessagingFailureReason.MessagingWindowExpired => ChannelDeliveryResult.Rejected("instagram.windowExpired"),
                MessagingFailureReason.TransportFailure => ChannelDeliveryResult.Rejected("instagram.unavailable"),
                MessagingFailureReason.MalformedResponse => ChannelDeliveryResult.Rejected("instagram.malformed"),
                _ => ChannelDeliveryResult.Rejected("instagram.rejected"),
            };
    }

    [LoggerMessage(EventId = 7100, Level = LogLevel.Information,
        Message = "Reply rejected: workspace has no connected Instagram account.")]
    private static partial void LogNoAccount(ILogger logger);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Warning,
        Message = "Reply rejected: connected Instagram account has no stored access token.")]
    private static partial void LogTokenMissing(ILogger logger);
}

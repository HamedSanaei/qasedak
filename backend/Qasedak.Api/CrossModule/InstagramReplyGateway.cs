using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Conversations.Application.Conversations;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Binds Conversations' channel-neutral gateway to Instagram: resolves the exact
/// connected account named by the request's opaque channel-account identity, verifies
/// workspace ownership and account state, decrypts only that account's stored token
/// and sends via the messaging adapter. There is no first-active-account fallback:
/// every failure refuses without touching another account. Failure codes are stable
/// and log-safe; token material never crosses this boundary.
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

        if (request.ChannelAccountId is not { IsResolved: true })
        {
            LogAccountUnresolved(logger);
            return ChannelDeliveryResult.Rejected("instagram.accountUnresolved");
        }

        var account = await accounts.FindByIdAsync(request.ChannelAccountId.Value.Value, cancellationToken);
        if (account is null)
        {
            LogUnknownAccount(logger);
            return ChannelDeliveryResult.Rejected("instagram.unknownAccount");
        }

        if (account.WorkspaceId != request.WorkspaceId)
        {
            LogWorkspaceMismatch(logger);
            return ChannelDeliveryResult.Rejected("instagram.accountWorkspaceMismatch");
        }

        if (account.IsDisconnected)
        {
            LogAccountDisconnected(logger);
            return ChannelDeliveryResult.Rejected("instagram.accountDisconnected");
        }

        if (account.Path != ConnectionPath.InstagramLogin)
        {
            LogUnsupportedPath(logger);
            return ChannelDeliveryResult.Rejected("instagram.unsupportedAccountPath");
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
        Message = "Reply rejected: request carries no resolved channel account.")]
    private static partial void LogAccountUnresolved(ILogger logger);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Warning,
        Message = "Reply rejected: channel account is unknown.")]
    private static partial void LogUnknownAccount(ILogger logger);

    [LoggerMessage(EventId = 7103, Level = LogLevel.Warning,
        Message = "Reply rejected: channel account belongs to another workspace.")]
    private static partial void LogWorkspaceMismatch(ILogger logger);

    [LoggerMessage(EventId = 7104, Level = LogLevel.Information,
        Message = "Reply rejected: channel account is disconnected.")]
    private static partial void LogAccountDisconnected(ILogger logger);

    [LoggerMessage(EventId = 7105, Level = LogLevel.Information,
        Message = "Reply rejected: channel account is not an Instagram Login connection.")]
    private static partial void LogUnsupportedPath(ILogger logger);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Warning,
        Message = "Reply rejected: connected Instagram account has no stored access token.")]
    private static partial void LogTokenMissing(ILogger logger);
}

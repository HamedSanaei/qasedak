namespace Qasedak.Modules.Conversations.Application.Conversations;

using Qasedak.BuildingBlocks.Domain;

/// <summary>Request to deliver an outbound reply over the thread's channel.</summary>
public sealed record ChannelDeliveryRequest(
    Guid WorkspaceId,
    string Channel,
    ChannelAccountId? ChannelAccountId,
    string ParticipantId,
    string Text);

/// <summary>Channel-agnostic delivery verdict; FailureCode is a stable, log-safe code.</summary>
public sealed record ChannelDeliveryResult(bool Accepted, string? FailureCode)
{
    public static ChannelDeliveryResult Delivered() => new(true, null);

    public static ChannelDeliveryResult Rejected(string failureCode) => new(false, failureCode);
}

/// <summary>
/// Outbound boundary of the Conversations module. The composition root binds it to the
/// channel-specific sender (Instagram today); the module itself never references one.
/// </summary>
public interface IConversationChannelGateway
{
    Task<ChannelDeliveryResult> DeliverAsync(ChannelDeliveryRequest request, CancellationToken cancellationToken = default);
}

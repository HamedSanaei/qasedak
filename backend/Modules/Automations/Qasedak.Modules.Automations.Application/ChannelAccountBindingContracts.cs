using Qasedak.BuildingBlocks.Domain;

namespace Qasedak.Modules.Automations.Application;

/// <summary>
/// Channel-neutral authoring guard for account-bound automations. The composition root
/// resolves the concrete provider account and proves workspace ownership before a binding
/// may be persisted. No provider token or transport call belongs at this boundary.
/// </summary>
public interface IChannelAccountBindingValidator
{
    Task<bool> IsOwnedActiveAccountAsync(
        Guid workspaceId,
        ChannelAccountId channelAccountId,
        CancellationToken cancellationToken = default);
}

/// <summary>Stable authoring failure used for unknown, foreign or inactive bindings.</summary>
public static class ChannelAccountBindingFailures
{
    public const string AccountNotFound = "automation.accountNotFound";
}

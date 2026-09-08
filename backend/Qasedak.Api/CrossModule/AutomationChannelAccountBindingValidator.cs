using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Instagram.Application.Accounts;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Composition-root ownership proof for automation authoring. Reads only the exact
/// ConnectedAccount aggregate; token material and Meta HTTP are intentionally unreachable.
/// </summary>
public sealed class AutomationChannelAccountBindingValidator(
    IConnectedAccountRepository accounts) : IChannelAccountBindingValidator
{
    public async Task<bool> IsOwnedActiveAccountAsync(
        Guid workspaceId,
        ChannelAccountId channelAccountId,
        CancellationToken cancellationToken = default)
    {
        if (!channelAccountId.IsResolved)
        {
            return false;
        }

        var account = await accounts.FindByIdAsync(channelAccountId.Value, cancellationToken);
        return account is not null && account.WorkspaceId == workspaceId && !account.IsDisconnected;
    }
}

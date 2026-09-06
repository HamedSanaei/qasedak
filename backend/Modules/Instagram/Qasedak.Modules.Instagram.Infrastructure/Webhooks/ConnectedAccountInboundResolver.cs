using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Webhooks;

namespace Qasedak.Modules.Instagram.Infrastructure.Webhooks;

/// <summary>
/// Maps the deterministic M13-002 active-account resolver into the webhook enrichment
/// boundary. One query per webhook entry; Resolved/NotFound/Ambiguous are preserved
/// exactly — no first-match, no disconnected rows, no guessing (ADR-011 correction).
/// </summary>
public sealed class ConnectedAccountInboundResolver(IConnectedAccountRepository accounts) : IInboundAccountResolver
{
    public async Task<InboundAccountResolution> ResolveAsync(string? providerAccountId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerAccountId))
        {
            return InboundAccountResolution.NotFound();
        }

        var resolution = await accounts.ResolveActiveAccountAsync(providerAccountId, cancellationToken);
        return resolution.Status switch
        {
            AccountResolutionStatus.Resolved when resolution.Account is not null =>
                InboundAccountResolution.Resolved(resolution.Account.WorkspaceId, resolution.Account.Id),
            AccountResolutionStatus.Ambiguous => InboundAccountResolution.Ambiguous(),
            _ => InboundAccountResolution.NotFound(),
        };
    }
}

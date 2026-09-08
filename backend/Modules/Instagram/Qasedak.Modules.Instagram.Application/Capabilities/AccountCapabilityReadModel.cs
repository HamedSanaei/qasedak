using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Modules.Instagram.Application.Capabilities;

/// <summary>Qasedak product capabilities exposed to the frontend for one exact account.</summary>
public enum InstagramCapability
{
    MediaCatalog,
    Analytics,
    Messaging,
    CommentAutomation,
    PrivateReply,
    PublicReply,
    RevealFlow,
    FollowGate,
    ConversationHistorySync,
}

/// <summary>Server-owned availability; the browser never derives this from raw provider scopes.</summary>
public enum InstagramCapabilityState
{
    Available,
    PermissionRequired,
    Disconnected,
    Unhealthy,
    TemporarilyUnavailable,
    Unsupported,
}

public sealed record InstagramCapabilityItem(
    InstagramCapability Capability,
    InstagramCapabilityState State,
    string? ReasonCode = null);

public sealed record AccountCapabilitiesRecord(
    Guid AccountId,
    IReadOnlyList<InstagramCapabilityItem> Capabilities);

public abstract record AccountCapabilitiesResult
{
    public sealed record Ok(AccountCapabilitiesRecord Value) : AccountCapabilitiesResult;
    public sealed record NotFound : AccountCapabilitiesResult;
}

/// <summary>
/// Pure local policy. It uses only persisted account state, the OAuth permission snapshot,
/// subscription health and known shipped Qasedak behavior. It never reads tokens or calls Meta.
/// </summary>
public static class InstagramCapabilityPolicy
{
    public const string ManageInsightsScope = "instagram_business_manage_insights";

    private sealed record Rule(
        InstagramCapability Capability,
        string[] RequiredScopes,
        bool RequiresHealthySubscription = false,
        bool Supported = true);

    private static readonly Rule[] Rules =
    [
        new(InstagramCapability.MediaCatalog, [InstagramAuthorizationScopes.Basic]),
        new(InstagramCapability.Analytics, [InstagramAuthorizationScopes.Basic, ManageInsightsScope]),
        new(InstagramCapability.Messaging, [InstagramAuthorizationScopes.Basic, InstagramAuthorizationScopes.ManageMessages], true),
        new(InstagramCapability.CommentAutomation, [InstagramAuthorizationScopes.Basic, InstagramAuthorizationScopes.ManageComments], true),
        new(InstagramCapability.PrivateReply, [InstagramAuthorizationScopes.Basic, InstagramAuthorizationScopes.ManageComments]),
        new(InstagramCapability.PublicReply, [InstagramAuthorizationScopes.Basic, InstagramAuthorizationScopes.ManageComments]),
        new(InstagramCapability.RevealFlow, [InstagramAuthorizationScopes.Basic, InstagramAuthorizationScopes.ManageComments, InstagramAuthorizationScopes.ManageMessages], true),
        // M13-011 intentionally keeps ordinary-postback follow lookup switched off until proven.
        new(InstagramCapability.FollowGate, [], Supported: false),
        new(InstagramCapability.ConversationHistorySync, [InstagramAuthorizationScopes.Basic, InstagramAuthorizationScopes.ManageMessages]),
    ];

    public static AccountCapabilitiesRecord Evaluate(ConnectedAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var granted = account.Scopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new AccountCapabilitiesRecord(
            account.Id,
            Rules.Select(rule => EvaluateRule(account, granted, rule)).ToArray());
    }

    private static InstagramCapabilityItem EvaluateRule(
        ConnectedAccount account,
        HashSet<string> granted,
        Rule rule)
    {
        if (!rule.Supported)
        {
            return new(rule.Capability, InstagramCapabilityState.Unsupported, "capability.unsupported");
        }

        if (account.IsDisconnected)
        {
            return new(rule.Capability, InstagramCapabilityState.Disconnected, "account.disconnected");
        }

        if (account.Health is AccountHealth.Expired or AccountHealth.Revoked or AccountHealth.Unhealthy)
        {
            return new(rule.Capability, InstagramCapabilityState.Unhealthy, "account.unhealthy");
        }

        if (rule.RequiredScopes.Any(scope => !granted.Contains(scope)))
        {
            return new(rule.Capability, InstagramCapabilityState.PermissionRequired, "capability.permissionRequired");
        }

        if (rule.RequiresHealthySubscription && account.SubscriptionHealth is not SubscriptionHealth.Healthy)
        {
            return new(rule.Capability, InstagramCapabilityState.TemporarilyUnavailable, "capability.subscriptionUnavailable");
        }

        return new(rule.Capability, InstagramCapabilityState.Available);
    }
}

/// <summary>Exact-account token-free capability projection.</summary>
public sealed class GetAccountCapabilitiesUseCase(IConnectedAccountRepository accounts)
{
    public async Task<AccountCapabilitiesResult> ExecuteAsync(
        Guid workspaceId,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindByIdAsync(accountId, cancellationToken);
        if (account is null || account.WorkspaceId != workspaceId)
        {
            return new AccountCapabilitiesResult.NotFound();
        }

        return new AccountCapabilitiesResult.Ok(InstagramCapabilityPolicy.Evaluate(account));
    }
}

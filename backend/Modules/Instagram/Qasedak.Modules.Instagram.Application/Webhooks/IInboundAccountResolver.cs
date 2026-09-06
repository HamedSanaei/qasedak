namespace Qasedak.Modules.Instagram.Application.Webhooks;

/// <summary>
/// Exact-account resolution for inbound webhook routing (M13-008). Reuses the one
/// deterministic M13-002 primitive (<c>ResolveActiveAccountAsync</c>) behind a focused
/// port so normalization stays pure and the processing boundary can enrich events with the
/// Qasedak account key. Never guesses: unknown and ambiguous identities resolve to no
/// account and dispatch nothing.
/// </summary>
public interface IInboundAccountResolver
{
    Task<InboundAccountResolution> ResolveAsync(string? providerAccountId, CancellationToken cancellationToken = default);
}

public enum InboundAccountStatus
{
    /// <summary>Exactly one active connected account carries the routing identity.</summary>
    Resolved,

    /// <summary>No active account carries the identity (unknown or disconnected-only).</summary>
    NotFound,

    /// <summary>Several active accounts carry the identity: fail closed, never choose.</summary>
    Ambiguous,
}

public sealed record InboundAccountResolution(
    InboundAccountStatus Status,
    Guid? WorkspaceId,
    Guid? ConnectedAccountId)
{
    public static InboundAccountResolution Resolved(Guid workspaceId, Guid connectedAccountId) =>
        new(InboundAccountStatus.Resolved, workspaceId, connectedAccountId);

    public static InboundAccountResolution NotFound() => new(InboundAccountStatus.NotFound, null, null);

    public static InboundAccountResolution Ambiguous() => new(InboundAccountStatus.Ambiguous, null, null);
}

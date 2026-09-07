using Qasedak.BuildingBlocks.Domain;

namespace Qasedak.Modules.Automations.Application;

/// <summary>
/// Channel-neutral verdict on whether a delayed Direct Message may be attempted for an
/// exact (account, participant) pair (M13-012 §49-50). The composition root implements
/// this via provider-projected state (e.g. latest inbound message time per account +
/// participant and connected-account availability); provider error DTOs never cross this
/// boundary. Meta remains the final authority at send time even when this port says
/// <see cref="Eligible"/>.
/// </summary>
public enum DirectEligibility
{
    /// <summary>Local evidence shows an open window and a usable account.</summary>
    Eligible,

    /// <summary>Local evidence shows the 24h response window has expired.</summary>
    WindowExpired,

    /// <summary>The exact connected account is unavailable (missing/disconnected/no token).</summary>
    AccountUnavailable,

    /// <summary>No authoritative inbound participant identity/evidence exists.</summary>
    RecipientUnavailable,

    /// <summary>The projection could not be evaluated (transient); safe to re-check later.</summary>
    Unknown,
}

/// <summary>Port consumed by delayed follow-up execution before any provider mutation.</summary>
public interface IDirectEligibilityPort
{
    Task<DirectEligibility> EvaluateAsync(
        Guid workspaceId,
        ChannelAccountId channelAccountId,
        string participantId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken = default);
}

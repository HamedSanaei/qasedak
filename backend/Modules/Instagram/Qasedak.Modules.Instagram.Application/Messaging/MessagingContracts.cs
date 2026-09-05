namespace Qasedak.Modules.Instagram.Application.Messaging;

/// <summary>Structured reasons for a rejected/failed Instagram message send.</summary>
public enum MessagingFailureReason
{
    /// <summary>Network-level failure; the request never reached Meta or no answer arrived.</summary>
    TransportFailure,

    /// <summary>Meta answered with an error payload outside the known special cases.</summary>
    RejectedByMeta,

    /// <summary>
    /// Meta refused because the recipient is outside the 24-hour customer service window
    /// (official Graph code 10 + subcode 2534022). Distinct so callers can schedule
    /// instead of retrying blindly.
    /// </summary>
    MessagingWindowExpired,

    /// <summary>Meta answered successfully but the payload did not match the contract.</summary>
    MalformedResponse,
}

public sealed record MessagingSendResult
{
    public bool Succeeded { get; }

    public MessagingFailure? Failure { get; }

    private MessagingSendResult(bool succeeded, MessagingFailure? failure)
    {
        Succeeded = succeeded;
        Failure = failure;
    }

    public static MessagingSendResult Ok() => new(true, null);

    public static MessagingSendResult Fail(MessagingFailureReason reason, string detail) =>
        new(false, new MessagingFailure(reason, detail));
}

public sealed record MessagingFailure(MessagingFailureReason Reason, string Detail);

/// <summary>
/// Port to Instagram's messaging send API. Implementations must never log token material;
/// failures are structured results, never exceptions across this boundary.
/// </summary>
public interface IInstagramMessagingClient
{
    Task<MessagingSendResult> SendTextAsync(
        string accessToken,
        string recipientProviderUserId,
        string text,
        CancellationToken cancellationToken = default);
}

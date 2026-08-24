namespace Qasedak.Modules.Billing.Domain.Payments;

/// <summary>Lifecycle of a single checkout attempt. Terminal states are Verified/Failed.</summary>
public enum PaymentAttemptStatus
{
    /// <summary>Created server-side; the provider has not yet reported back.</summary>
    Pending = 1,

    /// <summary>Provider-verified server-to-server; entitlement applied exactly once.</summary>
    Verified = 2,

    /// <summary>Terminal failure (canceled at provider, verification rejected, timeout).</summary>
    Failed = 3,
}

/// <summary>
/// One durable checkout attempt against one payment provider.
///
/// Invariants:
/// - amounts are always stored in the canonical Qasedak currency (IRR — see ADR-008);
///   conversion to a provider's unit happens only inside its adapter;
/// - the provider authority is unique across all attempts when present (callback replay
///   resolves to exactly one attempt);
/// - only a Pending attempt may transition to terminal states, and each transition happens
///   once — persistence enforces this with an optimistic concurrency token so concurrent
///   callbacks/retries cannot double-apply entitlements;
/// - no card data is ever stored beyond the provider's masked PAN/hash, retained for audit;
/// - timestamps are parameters (no clock in the Domain).
/// </summary>
public sealed class PaymentAttempt
{
    public const int MaxAuthorityLength = 128;

    public const int MaxReferenceLength = 64;

    private PaymentAttempt()
    {
    }

    public Guid Id { get; private init; }

    public Guid WorkspaceId { get; private init; }

    /// <summary>Purchasable intent: which plan the workspace was checking out for.</summary>
    public Guid PlanId { get; private init; }

    /// <summary>Provider identifier ("zarinpal", "mellat"); resolved through the gateway registry.</summary>
    public string ProviderId { get; private init; } = string.Empty;

    /// <summary>Server-authoritative amount in IRR captured from the plan at creation.</summary>
    public long AmountIrr { get; private init; }

    public PaymentAttemptStatus Status { get; private set; }

    /// <summary>Provider token returned by the payment request; null until initialization succeeds.</summary>
    public string? Authority { get; private set; }

    /// <summary>Provider transaction reference (e.g. Zarinpal ref_id); null until verified.</summary>
    public string? ProviderReferenceId { get; private set; }

    /// <summary>Stable failure code when Status=Failed ("payment.canceledByUser", "payment.verifyRejected", …).</summary>
    public string? FailureCode { get; private set; }

    /// <summary>Masked PAN from the provider (e.g. "502229******5995"); audit-only.</summary>
    public string? MaskedCardPan { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private init; }

    public DateTimeOffset? VerifiedAtUtc { get; private set; }

    public DateTimeOffset? FailedAtUtc { get; private set; }

    /// <summary>Raised on every state transition; persistence maps it to xmin for concurrency.</summary>
    public uint Version { get; private set; }

    public static PaymentAttempt Create(
        Guid id,
        Guid workspaceId,
        Guid planId,
        string providerId,
        long amountIrr,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new BillingDomainException("billing.invalidPaymentId", "A payment attempt requires an id.");
        }

        if (workspaceId == Guid.Empty)
        {
            throw new BillingDomainException("billing.paymentWorkspaceRequired", "A payment attempt requires a workspace.");
        }

        if (planId == Guid.Empty)
        {
            throw new BillingDomainException("billing.paymentPlanRequired", "A payment attempt requires a plan intent.");
        }

        var normalizedProvider = NormalizeProvider(providerId);
        if (amountIrr <= 0)
        {
            throw new BillingDomainException("billing.paymentAmountInvalid", "A payment attempt requires a positive amount.");
        }

        return new PaymentAttempt
        {
            Id = id,
            WorkspaceId = workspaceId,
            PlanId = planId,
            ProviderId = normalizedProvider,
            AmountIrr = amountIrr,
            Status = PaymentAttemptStatus.Pending,
            CreatedAtUtc = createdAtUtc,
        };
    }

    /// <summary>Attaches the provider authority after a successful payment request.</summary>
    public void AttachAuthority(string authority)
    {
        EnsureStatus(PaymentAttemptStatus.Pending, "attach an authority to");
        var trimmed = authority?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxAuthorityLength)
        {
            throw new BillingDomainException("billing.paymentAuthorityInvalid", "The provider authority is missing or too long.");
        }

        Authority = trimmed;
    }

    /// <summary>
    /// Marks the attempt verified after server-to-server provider verification. Throws on
    /// any non-Pending state so duplicate callbacks surface as concurrency/idempotency
    /// signals instead of silently re-applying entitlements.
    /// </summary>
    public void MarkVerified(string providerReferenceId, string? maskedCardPan, DateTimeOffset verifiedAtUtc)
    {
        EnsureStatus(PaymentAttemptStatus.Pending, "verify");
        var reference = providerReferenceId?.Trim() ?? string.Empty;
        if (reference.Length == 0 || reference.Length > MaxReferenceLength)
        {
            throw new BillingDomainException("billing.paymentReferenceInvalid", "A verified payment requires the provider reference.");
        }

        Status = PaymentAttemptStatus.Verified;
        ProviderReferenceId = reference;
        MaskedCardPan = maskedCardPan;
        VerifiedAtUtc = verifiedAtUtc;
    }

    /// <summary>Marks a terminal failure (user canceled at provider, verify rejected, gateway outage).</summary>
    public void MarkFailed(string failureCode, DateTimeOffset failedAtUtc)
    {
        EnsureStatus(PaymentAttemptStatus.Pending, "fail");
        var code = failureCode?.Trim() ?? string.Empty;
        if (code.Length == 0)
        {
            throw new BillingDomainException("billing.paymentFailureCodeRequired", "A failed payment requires a stable failure code.");
        }

        Status = PaymentAttemptStatus.Failed;
        FailureCode = code;
        FailedAtUtc = failedAtUtc;
    }

    public bool IsTerminal => Status is PaymentAttemptStatus.Verified or PaymentAttemptStatus.Failed;

    /// <summary>Rehydration for persistence; state was valid when saved.</summary>
    public static PaymentAttempt FromState(
        Guid id,
        Guid workspaceId,
        Guid planId,
        string providerId,
        long amountIrr,
        PaymentAttemptStatus status,
        string? authority,
        string? providerReferenceId,
        string? failureCode,
        string? maskedCardPan,
        DateTimeOffset createdAtUtc,
        DateTimeOffset? verifiedAtUtc,
        DateTimeOffset? failedAtUtc) => new()
        {
            Id = id,
            WorkspaceId = workspaceId,
            PlanId = planId,
            ProviderId = providerId,
            AmountIrr = amountIrr,
            Status = status,
            Authority = authority,
            ProviderReferenceId = providerReferenceId,
            FailureCode = failureCode,
            MaskedCardPan = maskedCardPan,
            CreatedAtUtc = createdAtUtc,
            VerifiedAtUtc = verifiedAtUtc,
            FailedAtUtc = failedAtUtc,
        };

    private void EnsureStatus(PaymentAttemptStatus expected, string operation)
    {
        if (Status != expected)
        {
            throw new BillingDomainException(
                "billing.paymentWrongState",
                $"Cannot {operation} a payment attempt in state {Status}; expected {expected}.");
        }
    }

    private static string NormalizeProvider(string? providerId)
    {
        var normalized = providerId?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new BillingDomainException("billing.paymentProviderRequired", "A payment attempt requires a provider.");
        }

        return normalized;
    }
}

/// <summary>Stable failure codes surfaced by the payments feature.</summary>
public static class PaymentFailures
{
    public const string NotFound = "payment.notFound";

    public const string WrongState = "billing.paymentWrongState";

    public const string PlanNotFound = "billing.planNotFound";

    public const string PlanNotPurchasable = "billing.planNotPurchasable";

    public const string ProviderUnknown = "payment.providerUnknown";

    public const string ProviderDisabled = "payment.providerDisabled";

    public const string ProviderUnavailable = "payment.providerUnavailable";

    public const string RequestRejected = "payment.requestRejected";

    public const string VerifyRejected = "payment.verifyRejected";

    public const string CanceledByUser = "payment.canceledByUser";

    public const string AlreadyVerifiedElsewhere = "payment.authorityTaken";
}

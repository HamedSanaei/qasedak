namespace Qasedak.Modules.Billing.Application.Payments;

/// <summary>
/// Provider-neutral payment abstraction (ADR-008). Domain/Application code depends only on
/// this port; Infrastructure owns every provider-specific protocol detail. Amounts cross
/// this boundary in the canonical Qasedak currency (IRR); adapters convert only when a
/// provider's official contract demands a different unit.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Stable provider identifier ("zarinpal", "melli").</summary>
    string ProviderId { get; }

    /// <summary>
    /// Starts a payment at the provider and returns the authority plus the browser
    /// redirect URL. Implementations must be idempotent-safe: failures throw typed
    /// exceptions that the use case maps to stable failure codes.
    /// </summary>
    Task<PaymentInitialization> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Server-to-server verification. NEVER trust callback query values alone; the result
    /// of this call is the only accepted proof of payment.
    /// </summary>
    Task<PaymentVerificationResult> VerifyAsync(VerifyPaymentRequest request, CancellationToken cancellationToken = default);
}

public sealed record CreatePaymentRequest(
    Guid AttemptId,
    long AmountIrr,
    string Description,
    string CallbackUrl);

public sealed record PaymentInitialization(string ProviderId, string Authority, string RedirectUrl);

public sealed record VerifyPaymentRequest(string Authority, long AmountIrr);

/// <summary>Outcome semantics: Verified = first successful verify; AlreadyVerified = provider reports the transaction was verified before (idempotent replay); Failed = rejected/canceled.</summary>
public enum PaymentVerificationOutcome
{
    Verified = 1,

    AlreadyVerified = 2,

    Failed = 3,
}

public sealed record PaymentVerificationResult(
    PaymentVerificationOutcome Outcome,
    int? ProviderCode,
    string? ProviderReferenceId,
    string? MaskedCardPan,
    string? CardHash,
    string? ErrorDetail)
{
    public static PaymentVerificationResult Verified(int? code, string referenceId, string? maskedPan, string? cardHash) =>
        new(PaymentVerificationOutcome.Verified, code, referenceId, maskedPan, cardHash, null);

    public static PaymentVerificationResult AlreadyVerified(int? code) =>
        new(PaymentVerificationOutcome.AlreadyVerified, code, null, null, null, null);

    public static PaymentVerificationResult Failed(int? code, string detail) =>
        new(PaymentVerificationOutcome.Failed, code, null, null, null, detail);
}

/// <summary>Thrown by adapters for transport-level failures (timeout, 5xx, malformed body).</summary>
public sealed class PaymentGatewayUnavailableException(string message) : Exception(message);

/// <summary>Thrown when the provider rejects a well-formed request (config/contract error).</summary>
public sealed class PaymentRequestRejectedException(int? providerCode, string message) : Exception(message)
{
    public int? ProviderCode { get; } = providerCode;
}

/// <summary>Thrown when an adapter is asked to operate while its configuration is disabled/incomplete.</summary>
public sealed class PaymentProviderDisabledException(string providerId)
    : Exception($"The payment provider '{providerId}' is disabled or not configured.");

/// <summary>Unknown provider id at resolution time (distinct from known-but-disabled).</summary>
public sealed class PaymentProviderUnknownException(string providerId)
    : Exception($"The payment provider '{providerId}' is not supported.");

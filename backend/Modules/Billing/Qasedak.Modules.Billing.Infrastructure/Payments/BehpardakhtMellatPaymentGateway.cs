using Microsoft.Extensions.Options;
using Qasedak.Modules.Billing.Application.Payments;

namespace Qasedak.Modules.Billing.Infrastructure.Payments;

/// <summary>
/// Typed configuration contract for the Behpardakht Mellat internet payment gateway.
/// The HUMAN selected this provider (2026-08-24 decision, ADR-009), but the CURRENT
/// official merchant technical contract is NOT available to the project: only mirrored
/// historical PGW manuals (v1.0/1.1) and community packages describe the legacy
/// bpPayRequest → redirect → callback → bpVerifyRequest → bpSettleRequest flow. Per the
/// payment directive those are architectural background only — no endpoint, SOAP/WSDL
/// detail, response code or field semantic may be copied from them into production
/// transport. This adapter therefore exposes only its configuration boundary; every live
/// transport member fails CLOSED until the current official document supplies:
///
///   1. the current official service endpoint/WSDL (or REST equivalent) specification;
///   2. the exact payment-request / verify / settle operation contracts and their field
///      names as they exist TODAY;
///   3. the authoritative response-code table and callback parameter contract;
///   4. reversal/inquiry semantics if the current contract requires them.
///
/// Nothing here invents protocol behavior — see docs/architecture/ADR-009-payment-provider-behpardakht-mellat.md.
/// </summary>
public sealed class BehpardakhtOptions
{
    public const string SectionName = "Billing:Payments:Mellat";

    /// <summary>Whether checkout may select this provider (requires the verified contract first).</summary>
    public bool Enabled { get; set; }

    /// <summary>Terminal identifier assigned by Behpardakht/Mellat (historical concept — confirm against the verified contract).</summary>
    public string TerminalId { get; set; } = string.Empty;

    /// <summary>Merchant portal username (historical concept — confirm against the verified contract).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Merchant portal password (historical concept — confirm against the verified contract). Server-side secret.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Official API base URL — supplied by the verified merchant documentation.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Absolute base of the public callback endpoint.</summary>
    public string CallbackBaseUrl { get; set; } = string.Empty;
}

/// <summary>
/// Boundary implementation for the selected Behpardakht Mellat provider. Registered so
/// that configuration binding, resolver wiring, persistence and checkout plumbing are
/// complete, but it refuses to operate until Enabled is set by an operator who has the
/// CURRENT official Behpardakht merchant contract — the flag is intentionally false in
/// every shipped environment file.
/// </summary>
public sealed class BehpardakhtMellatPaymentGateway(IOptions<BehpardakhtOptions> options) : IPaymentGateway
{
    public const string ProviderIdValue = "mellat";

    private readonly BehpardakhtOptions _options = options.Value;

    public string ProviderId => ProviderIdValue;

    public Task<PaymentInitialization> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        EnsureVerifiedContractAvailable();
        throw new PaymentProviderDisabledException(ProviderIdValue);
    }

    public Task<PaymentVerificationResult> VerifyAsync(VerifyPaymentRequest request, CancellationToken cancellationToken = default)
    {
        EnsureVerifiedContractAvailable();
        throw new PaymentProviderDisabledException(ProviderIdValue);
    }

    private void EnsureVerifiedContractAvailable()
    {
        if (!_options.Enabled)
        {
            throw new PaymentProviderDisabledException(ProviderIdValue);
        }

        // An operator flipping Enabled without supplying the verified current protocol
        // would be a fabrication hazard: refuse loudly rather than guess the wire format.
        throw new PaymentGatewayUnavailableException(
            "Behpardakht Mellat live transport requires the CURRENT official merchant technical contract (service endpoints/WSDL, payment/verify/settle operation contracts, response-code table, callback schema), which is not available in this repository.");
    }
}

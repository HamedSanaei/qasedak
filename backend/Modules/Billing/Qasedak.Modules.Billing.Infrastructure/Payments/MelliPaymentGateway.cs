using Microsoft.Extensions.Options;
using Qasedak.Modules.Billing.Application.Payments;

namespace Qasedak.Modules.Billing.Infrastructure.Payments;

/// <summary>
/// Typed configuration contract for the Bank Melli (SADAD family) direct internet payment
/// gateway. The HUMAN selected this provider, but the official merchant technical contract
/// (endpoint list, request signing/encryption algorithm, token lifecycle, field names) is
/// NOT available in the repository. Per the payment directive this adapter therefore
/// exposes only its configuration boundary; every live transport member fails CLOSED with
/// PaymentProviderDisabledException until the official document supplies:
///
///   1. the official SADAD/Melli endpoint specification (payment request + verify URLs);
///   2. the exact authentication/signature algorithm and key material names;
///   3. callback parameter contract and verification response schema.
///
/// Nothing here invents algorithms — see docs/architecture/ADR-008-payment-providers.md.
/// </summary>
public sealed class MelliOptions
{
    public const string SectionName = "Billing:Payments:Melli";

    /// <summary>Whether checkout may select this provider (requires the official contract first).</summary>
    public bool Enabled { get; set; }

    /// <summary>Merchant identifier assigned by Bank Melli.</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>Terminal identifier assigned by Bank Melli.</summary>
    public string TerminalId { get; set; } = string.Empty;

    /// <summary>Secret credential material required by the official contract (server-side only).</summary>
    public string CredentialKey { get; set; } = string.Empty;

    /// <summary>Official API base URL — supplied by the merchant documentation.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Absolute base of the public callback endpoint.</summary>
    public string CallbackBaseUrl { get; set; } = string.Empty;
}

/// <summary>
/// Boundary implementation for the selected Bank Melli provider. Registered so that
/// configuration binding and resolver wiring are complete, but it refuses to operate
/// until Enabled is set by an operator who has the official SADAD contract — the flag is
/// intentionally false in every shipped environment file.
/// </summary>
public sealed class MelliPaymentGateway(IOptions<MelliOptions> options) : IPaymentGateway
{
    public const string ProviderIdValue = "melli";

    private readonly MelliOptions _options = options.Value;

    public string ProviderId => ProviderIdValue;

    public Task<PaymentInitialization> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOfficialContractAvailable();
        throw new PaymentProviderDisabledException(ProviderIdValue);
    }

    public Task<PaymentVerificationResult> VerifyAsync(VerifyPaymentRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOfficialContractAvailable();
        throw new PaymentProviderDisabledException(ProviderIdValue);
    }

    private void EnsureOfficialContractAvailable()
    {
        if (!_options.Enabled)
        {
            throw new PaymentProviderDisabledException(ProviderIdValue);
        }

        // An operator flipping Enabled without supplying the official protocol would be a
        // fabrication hazard: refuse loudly rather than guess the wire format.
        throw new PaymentGatewayUnavailableException(
            "Bank Melli/SADAD live transport requires the official merchant technical contract (endpoints, signing algorithm, callback schema) which is not available in this repository.");
    }
}

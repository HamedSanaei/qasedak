using Microsoft.Extensions.Options;

namespace Qasedak.Modules.Billing.Infrastructure.Payments;

/// <summary>
/// Typed configuration for the Zarinpal adapter. Values come from environment/secrets —
/// never from repository files. See docs/ops/DEPLOYMENT.md for the environment contract.
/// </summary>
public sealed class ZarinpalOptions
{
    public const string SectionName = "Billing:Payments:Zarinpal";

    /// <summary>Whether the provider may be selected at checkout.</summary>
    public bool Enabled { get; set; }

    /// <summary>The 36-character merchant code issued by Zarinpal (secret).</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>Payment API base. Production: https://payment.zarinpal.com — sandbox swaps this.</summary>
    public string BaseUrl { get; set; } = "https://payment.zarinpal.com";

    /// <summary>Absolute base of the public callback endpoint, e.g. https://api.qasedak.ir.</summary>
    public string CallbackBaseUrl { get; set; } = string.Empty;

    /// <summary>Explicit currency per official docs ("IRR" or "IRT"). Qasedak canonical is IRR.</summary>
    public string Currency { get; set; } = "IRR";
}

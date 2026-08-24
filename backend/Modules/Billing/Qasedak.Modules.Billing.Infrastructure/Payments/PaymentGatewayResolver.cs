using Microsoft.Extensions.Options;
using Qasedak.Modules.Billing.Application.Payments;

namespace Qasedak.Modules.Billing.Infrastructure.Payments;

/// <summary>
/// Resolves checkout provider ids to enabled gateways. Unknown providers fail closed;
/// disabled-but-known providers surface the typed disabled signal so the API can return
/// its stable code. Only gateways whose options are Enabled are listed as selectable.
/// </summary>
public sealed class PaymentGatewayResolver(
    ZarinpalPaymentGateway zarinpal,
    MelliPaymentGateway melli,
    IOptions<ZarinpalOptions> zarinpalOptions,
    IOptions<MelliOptions> melliOptions) : IPaymentGatewayResolver
{
    public IReadOnlyList<string> EnabledProviderIds
    {
        get
        {
            var enabled = new List<string>(2);
            if (zarinpalOptions.Value.Enabled)
            {
                enabled.Add(ZarinpalPaymentGateway.ProviderIdValue);
            }

            if (melliOptions.Value.Enabled)
            {
                enabled.Add(MelliPaymentGateway.ProviderIdValue);
            }

            return enabled;
        }
    }

    public IPaymentGateway Resolve(string providerId)
    {
        var normalized = providerId?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            ZarinpalPaymentGateway.ProviderIdValue => zarinpalOptions.Value.Enabled
                ? zarinpal
                : throw new PaymentProviderDisabledException(normalized),
            MelliPaymentGateway.ProviderIdValue => melliOptions.Value.Enabled
                ? melli
                : throw new PaymentProviderDisabledException(normalized),
            _ => throw new PaymentProviderUnknownException(normalized),
        };
    }
}

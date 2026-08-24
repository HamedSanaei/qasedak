using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Billing.Application.Payments;

namespace Qasedak.Modules.Billing.Infrastructure.Payments;

/// <summary>
/// Zarinpal payment-gateway adapter over the OFFICIAL current REST contract
/// (docs.zarinpal.com → درگاه پرداخت → راهنمای اتصال, captured 2026-08-24):
///
/// 1. POST {BaseUrl}/pg/v4/payment/request.json with merchant_id (36 chars), amount,
///    currency ("IRR"|"IRT"), description, callback_url, metadata.order_id —
///    success response data.code=100 + data.authority (+ fee_type/fee).
/// 2. The buyer is redirected to {BaseUrl}/pg/StartPay/{authority}.
/// 3. The provider returns the buyer to callback_url?Authority=…&Status=OK|NOK.
/// 4. POST {BaseUrl}/pg/v4/payment/verify.json with merchant_id + amount + authority —
///    first successful verify returns data.code=100 ("Verified") with ref_id and masked
///    card_pan/card_hash; verifying the SAME transaction again returns code=101
///    ("verified before" semantics) which this adapter maps to AlreadyVerified.
///
/// Transport details live here only; Domain/Application depend on IPaymentGateway alone.
/// Direct HttpClient integration via IHttpClientFactory — no community packages.
/// Secrets come from typed options bound to environment configuration; the merchant id is
/// never logged and request/response payloads are never logged raw.
/// </summary>
public sealed partial class ZarinpalPaymentGateway(
    HttpClient httpClient,
    IOptions<ZarinpalOptions> options,
    ILogger<ZarinpalPaymentGateway> logger) : IPaymentGateway
{
    public const string ProviderIdValue = "zarinpal";

    /// <summary>Named HttpClient registration (typed options configure its base address).</summary>
    public const string HttpClientName = "zarinpal-gateway";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ZarinpalOptions _options = options.Value;

    public string ProviderId => ProviderIdValue;

    public async Task<PaymentInitialization> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var payload = new
        {
            merchant_id = _options.MerchantId,
            amount = request.AmountIrr,
            currency = NormalizeCurrency(_options.Currency),
            description = Truncate(request.Description, 255),
            callback_url = request.CallbackUrl,
            metadata = new { order_id = request.AttemptId.ToString() },
        };

        var envelope = await PostAsync<ZarinpalRequestData>("/pg/v4/payment/request.json", payload, cancellationToken)
            .ConfigureAwait(false);

        if (envelope.Data?.Code == 100 && !string.IsNullOrWhiteSpace(envelope.Data.Authority))
        {
            Log.PaymentRequested(logger, attemptId: request.AttemptId);
            return new PaymentInitialization(
                ProviderIdValue,
                envelope.Data.Authority,
                $"{_options.BaseUrl.TrimEnd('/')}/pg/StartPay/{envelope.Data.Authority}");
        }

        // Contract-level rejection (invalid merchant, bad amount, ...): stable, safe detail.
        Log.PaymentRequestRejected(logger, attemptId: request.AttemptId, code: envelope.Data?.Code);
        throw new PaymentRequestRejectedException(
            envelope.Data?.Code,
            string.IsNullOrWhiteSpace(envelope.Data?.Message) ? "payment request rejected" : envelope.Data!.Message!);
    }

    public async Task<PaymentVerificationResult> VerifyAsync(VerifyPaymentRequest request, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var payload = new
        {
            merchant_id = _options.MerchantId,
            amount = request.AmountIrr,
            authority = request.Authority,
        };

        var envelope = await PostAsync<ZarinpalVerifyData>("/pg/v4/payment/verify.json", payload, cancellationToken)
            .ConfigureAwait(false);

        return envelope.Data?.Code switch
        {
            // First successful server-to-server verification.
            100 => PaymentVerificationResult.Verified(
                envelope.Data.Code,
                envelope.Data.ReferenceId?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    ?? throw new PaymentGatewayUnavailableException("Verified response without ref_id."),
                envelope.Data.CardPan,
                envelope.Data.CardHash),

            // Official docs: verifying an already-verified transaction returns 101.
            101 => PaymentVerificationResult.AlreadyVerified(envelope.Data.Code),

            _ => PaymentVerificationResult.Failed(
                envelope.Data?.Code,
                string.IsNullOrWhiteSpace(envelope.Data?.Message) ? "verification rejected" : envelope.Data!.Message!),
        };
    }

    /// <summary>Single POST helper mapping transport faults to the neutral unavailability signal.</summary>
    private async Task<ZarinpalEnvelope<TData>> PostAsync<TData>(string path, object payload, CancellationToken cancellationToken)
        where TData : class
    {
        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await httpClient.PostAsJsonAsync(path, payload, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Log.TransportTimeout(logger, path);
            throw new PaymentGatewayUnavailableException("The payment provider timed out.");
        }
        catch (HttpRequestException exception)
        {
            Log.TransportFailure(logger, path, exception.GetType().Name);
            throw new PaymentGatewayUnavailableException("The payment provider is unreachable.");
        }

        try
        {
            var envelope = await httpResponse.Content
                .ReadFromJsonAsync<ZarinpalEnvelope<TData>>(SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (envelope is null)
            {
                throw new PaymentGatewayUnavailableException("Empty payment provider response.");
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                Log.NonSuccessStatus(logger, path, (int)httpResponse.StatusCode);
                // Zarinpal signals business failures inside the JSON envelope; a non-2xx
                // status with no parsable envelope is treated as unavailable.
                if (envelope.Data is null)
                {
                    throw new PaymentGatewayUnavailableException($"Unexpected payment provider status {(int)httpResponse.StatusCode}.");
                }
            }

            return envelope;
        }
        catch (JsonException)
        {
            Log.MalformedResponse(logger, path);
            throw new PaymentGatewayUnavailableException("Malformed payment provider response.");
        }
    }

    private void EnsureConfigured()
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.MerchantId))
        {
            throw new PaymentProviderDisabledException(ProviderIdValue);
        }
    }

    private static string NormalizeCurrency(string currency) =>
        string.Equals(currency, "IRT", StringComparison.OrdinalIgnoreCase) ? "IRT" : "IRR";

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= maxLength ? value : value[..maxLength];

    internal sealed record ZarinpalEnvelope<TData>(
        [property: JsonPropertyName("data")] TData? Data,
        [property: JsonPropertyName("errors")] JsonElement? Errors)
        where TData : class;

    internal sealed record ZarinpalRequestData(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("authority")] string? Authority,
        [property: JsonPropertyName("fee_type")] string? FeeType,
        [property: JsonPropertyName("fee")] int? Fee);

    internal sealed record ZarinpalVerifyData(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("ref_id")] long? ReferenceId,
        [property: JsonPropertyName("card_pan")] string? CardPan,
        [property: JsonPropertyName("card_hash")] string? CardHash,
        [property: JsonPropertyName("fee_type")] string? FeeType,
        [property: JsonPropertyName("fee")] int? Fee);

    /// <summary>Structured, secret-free logging source.</summary>
    private static partial class Log
    {
        [LoggerMessage(1, LogLevel.Information, "Zarinpal payment requested (attempt {AttemptId}).")]
        public static partial void PaymentRequested(ILogger logger, Guid attemptId);

        [LoggerMessage(2, LogLevel.Warning, "Zarinpal payment request rejected (attempt {AttemptId}, code {Code}).")]
        public static partial void PaymentRequestRejected(ILogger logger, Guid attemptId, int? code);

        [LoggerMessage(3, LogLevel.Warning, "Zarinpal transport timeout on {Path}.")]
        public static partial void TransportTimeout(ILogger logger, string path);

        [LoggerMessage(4, LogLevel.Warning, "Zarinpal transport failure ({ExceptionType}) on {Path}.")]
        public static partial void TransportFailure(ILogger logger, string path, string exceptionType);

        [LoggerMessage(5, LogLevel.Warning, "Zarinpal non-success status {Status} on {Path}.")]
        public static partial void NonSuccessStatus(ILogger logger, string path, int status);

        [LoggerMessage(6, LogLevel.Error, "Malformed Zarinpal response on {Path}.")]
        public static partial void MalformedResponse(ILogger logger, string path);
    }
}

using System.Globalization;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Billing.Application.Payments;

namespace Qasedak.Modules.Billing.Infrastructure.Payments;

/// <summary>
/// Typed server-side configuration for the Behpardakht Mellat IPG transport, completed
/// against the project-owner-supplied vendor reference
/// docs/vendor/behpardakht/BEHPARDAKHT-IPG-v1.29-EN.md (User Guide v1.29, Tir 1402,
/// "Unofficial - External" provenance). URLs stay overridable so newer merchant-specific
/// onboarding instructions can replace them without code changes (§21.2 of the reference).
/// </summary>
public sealed class BehpardakhtOptions
{
    public const string SectionName = "Billing:Payments:Mellat";

    /// <summary>Whether checkout may select this provider.</summary>
    public bool Enabled { get; set; }

    /// <summary>Merchant Internet terminal number (vendor contract §4).</summary>
    public string TerminalId { get; set; } = string.Empty;

    /// <summary>Merchant username (SECRET).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Merchant password (SECRET).</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// SOAP Web Service URL from vendor contract §6.1; defaults to the documented
    /// production address.
    /// </summary>
    public string ServiceUrl { get; set; } = "https://bpm.shaparak.ir/pgwchannel/services/pgw?wsdl";

    /// <summary>Persian payment page from vendor contract §8.2; the redirect form posts RefId here.</summary>
    public string PaymentPageUrl { get; set; } = "https://bpm.shaparak.ir/pgwchannel/startpay.mellat";

    /// <summary>
    /// SOAP target namespace for request envelopes. The v1.29 guide defines operations by
    /// name but leaves WSDL binding details to the live WSDL document, so this value is
    /// configuration-overridable rather than hard-coded protocol behavior.
    /// </summary>
    public string ServiceNamespace { get; set; } = "http://interfaces.core.bpm.bpt.com/";

    /// <summary>Absolute base of the public callback endpoint (registered merchant domain per §5).</summary>
    public string CallbackBaseUrl { get; set; } = string.Empty;

    /// <summary>Outbound SOAP timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 20;
}

/// <summary>Vendor response-code classification (reference §19). Bounded and explicit.</summary>
internal enum BehpardakhtCodeClass
{
    /// <summary>ResCode 0 — operation succeeded.</summary>
    Success,

    /// <summary>43 already verified / 45 already settled — treat as idempotent success.</summary>
    IdempotentSuccess,

    /// <summary>17 — cardholder cancelled at the gateway page.</summary>
    UserCancelled,

    /// <summary>Terminal rejection per the documented table (never retried as-is).</summary>
    DefinitiveFailure,

    /// <summary>Merchant setup problem: invalid merchant/credentials/IP/domain (21, 23, 24, 62, 421).</summary>
    ConfigurationError,

    /// <summary>Ambiguous outcome (transport failure or undocumented code) — reconcile via Inquiry.</summary>
    UnknownState,
}

/// <summary>Strongly typed mapping of the v1.29 response-code table (§19).</summary>
internal static class BehpardakhtResponseCodes
{
    private static readonly HashSet<int> IdempotentSuccessCodes = new([43, 45]);

    private static readonly HashSet<int> ConfigurationErrorCodes = new([21, 23, 24, 62, 421]);

    private static readonly HashSet<int> DefinitiveFailureCodes = new(
    [
        11, 12, 13, 14, 15, 16, 18, 19,
        25, 31, 32, 33, 34, 35,
        41, 42, 44, 46, 47, 48,
        51, 54, 55, 61,
        98,
        111, 112, 113, 114,
        412, 413, 414, 415, 416, 417, 418, 419,
        995,
    ]);

    public static BehpardakhtCodeClass Classify(int resCode) => resCode switch
    {
        0 => BehpardakhtCodeClass.Success,
        17 => BehpardakhtCodeClass.UserCancelled,
        _ when IdempotentSuccessCodes.Contains(resCode) => BehpardakhtCodeClass.IdempotentSuccess,
        _ when ConfigurationErrorCodes.Contains(resCode) => BehpardakhtCodeClass.ConfigurationError,
        _ when DefinitiveFailureCodes.Contains(resCode) => BehpardakhtCodeClass.DefinitiveFailure,
        _ => BehpardakhtCodeClass.UnknownState,
    };

    /// <summary>Safe description for logs/failures — codes only, never credential material.</summary>
    public static string Describe(int resCode) => Classify(resCode) switch
    {
        BehpardakhtCodeClass.Success => "success",
        BehpardakhtCodeClass.IdempotentSuccess => "idempotent success",
        BehpardakhtCodeClass.UserCancelled => "user cancelled",
        BehpardakhtCodeClass.ConfigurationError => "merchant configuration error",
        BehpardakhtCodeClass.DefinitiveFailure => "declined",
        _ => "unknown state",
    };
}

/// <summary>Wire-level inputs for one bpPayRequest call (vendor contract §8.3, required subset).</summary>
internal sealed record BehpardakhtPayRequest(
    long TerminalId,
    string UserName,
    string Password,
    long OrderId,
    long AmountIrr,
    string LocalDate,
    string LocalTime,
    string AdditionalData,
    string CallBackUrl,
    string PayerId);

/// <summary>Wire-level inputs for verify/settle/inquiry/reversal calls (§10–13 required fields).</summary>
internal sealed record BehpardakhtTransactionRequest(
    long TerminalId,
    string UserName,
    string Password,
    long OrderId,
    long SaleOrderId,
    long SaleReferenceId);

internal sealed record BehpardakhtPayResult(int ResCode, string? RefId);

internal sealed record BehpardakhtCodeResult(int ResCode);

/// <summary>
/// Internal SOAP boundary behind the gateway (reference §21.8). Infrastructure-only:
/// no SOAP/XML type escapes into Application or Domain. Implementations must parse
/// defensively and translate transport failures into PaymentGatewayUnavailableException.
/// </summary>
internal interface IBehpardakhtSoapClient
{
    Task<BehpardakhtPayResult> PayAsync(BehpardakhtPayRequest request, CancellationToken cancellationToken = default);

    Task<BehpardakhtCodeResult> VerifyAsync(BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default);

    Task<BehpardakhtCodeResult> SettleAsync(BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default);

    Task<BehpardakhtCodeResult> InquiryAsync(BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default);

    Task<BehpardakhtCodeResult> ReverseAsync(BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Small explicit SOAP 1.1 client: builds the documented operation envelopes, posts them
/// to the configured service URL and parses only the documented return element. No
/// generated types, no external packages; XML handling stays inside this class.
/// </summary>
internal sealed class BehpardakhtSoapClient(HttpClient httpClient, IOptions<BehpardakhtOptions> options) : IBehpardakhtSoapClient
{
    private readonly BehpardakhtOptions _options = options.Value;

    public Task<BehpardakhtPayResult> PayAsync(BehpardakhtPayRequest request, CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            "bpPayRequest",
            [
                ("terminalId", request.TerminalId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("userName", request.UserName),
                ("userPassword", request.Password),
                ("orderId", request.OrderId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("amount", request.AmountIrr.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("localDate", request.LocalDate),
                ("localTime", request.LocalTime),
                ("additionalData", request.AdditionalData),
                ("callBackUrl", request.CallBackUrl),
                ("payerId", request.PayerId),
            ],
            static xml => ParsePayResponse(xml),
            cancellationToken);

    public Task<BehpardakhtCodeResult> VerifyAsync(BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default) =>
        ExecuteCodeAsync("bpVerifyRequest", request, cancellationToken);

    public Task<BehpardakhtCodeResult> SettleAsync(BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default) =>
        ExecuteCodeAsync("bpSettleRequest", request, cancellationToken);

    public Task<BehpardakhtCodeResult> InquiryAsync(BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default) =>
        ExecuteCodeAsync("bpInquiryRequest", request, cancellationToken);

    public Task<BehpardakhtCodeResult> ReverseAsync(BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default) =>
        ExecuteCodeAsync("bpReversalRequest", request, cancellationToken);

    private Task<BehpardakhtCodeResult> ExecuteCodeAsync(
        string operation, BehpardakhtTransactionRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync<BehpardakhtCodeResult>(
            operation,
            [
                ("terminalId", request.TerminalId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("userName", request.UserName),
                ("userPassword", request.Password),
                ("orderId", request.OrderId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("saleOrderId", request.SaleOrderId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                ("saleReferenceId", request.SaleReferenceId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ],
            xml => ParseCodeResponse(operation, xml),
            cancellationToken);

    private async Task<TResult> ExecuteAsync<TResult>(
        string operation,
        (string Name, string Value)[] parameters,
        Func<string, TResult> parseReturn,
        CancellationToken cancellationToken)
    {
        var envelope = BuildEnvelope(operation, parameters, _options.ServiceNamespace);
        using var content = new StringContent(envelope, System.Text.Encoding.UTF8, "text/xml");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.ServiceUrl)
        {
            Content = content,
        };
        httpRequest.Headers.TryAddWithoutValidation("SOAPAction", $"urn:{operation}");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(httpRequest, cancellationToken);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PaymentGatewayUnavailableException($"The Behpardakht service timed out during {operation}.");
        }
        catch (HttpRequestException exception)
        {
            throw new PaymentGatewayUnavailableException($"The Behpardakht service is unreachable during {operation}. {exception.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new PaymentGatewayUnavailableException($"The Behpardakht service returned HTTP {(int)response.StatusCode} during {operation}.");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // Defensive: a SOAP fault is a provider-side protocol failure, not a payment answer.
            if (body.Contains(":Fault", StringComparison.OrdinalIgnoreCase) || body.Contains("<Fault>", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentGatewayUnavailableException($"The Behpardakht service returned a SOAP fault during {operation}.");
            }

            var returnValue = ExtractReturnElement(body, operation);
            if (returnValue is null)
            {
                throw new PaymentGatewayUnavailableException($"The Behpardakht service returned a malformed response during {operation}.");
            }

            return parseReturn(returnValue);
        }
    }

    internal static string BuildEnvelope(string operation, (string Name, string Value)[] parameters, string serviceNamespace)
    {
        // Values are XML-escaped so credential/user data can never break out of the element.
        var body = string.Concat(parameters.Select(p =>
            $"<{p.Name}>{System.Security.SecurityElement.Escape(p.Value)}</{p.Name}>"));
        return
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\" " +
            "xmlns:tem=\"" + System.Security.SecurityElement.Escape(serviceNamespace) + "\">" +
            "<soap:Body><tem:" + operation + ">" + body + "</tem:" + operation + "></soap:Body></soap:Envelope>";
    }

    /// <summary>Namespace-agnostic extraction of the documented &lt;return&gt; child of the response element.</summary>
    internal static string? ExtractReturnElement(string responseBody, string operation)
    {
        try
        {
            var document = System.Xml.Linq.XDocument.Parse(responseBody);
            var responseElement = document
                .Descendants()
                .FirstOrDefault(e => e.Name.LocalName == $"{operation}Response");
            var returnElement = responseElement?
                .Elements()
                .FirstOrDefault(e => e.Name.LocalName == "return")
                ?? responseElement?
                    .Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "return");
            return returnElement?.Value;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    /// <summary>"ResCode,RefId" parsing per vendor contract §8.1 — defensive, case preserved.</summary>
    internal static BehpardakhtPayResult ParsePayResponse(string returnValue)
    {
        var parts = returnValue.Split(',', count: 2);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0].Trim(), out var resCode))
        {
            throw new PaymentGatewayUnavailableException("The Behpardakht pay response was malformed.");
        }

        var refId = parts[1].Trim();
        return new BehpardakhtPayResult(resCode, refId.Length == 0 ? null : refId);
    }

    internal static BehpardakhtCodeResult ParseCodeResponse(string operation, string returnValue)
    {
        if (!int.TryParse(returnValue.Trim(), out var resCode))
        {
            throw new PaymentGatewayUnavailableException($"The Behpardakht {operation} response was malformed.");
        }

        return new BehpardakhtCodeResult(resCode);
    }
}

/// <summary>
/// Behpardakht Mellat transport implementing the vendor reference
/// docs/vendor/behpardakht/BEHPARDAKHT-IPG-v1.29-EN.md behind the provider-neutral
/// IPaymentGateway port. Flow per the contract: bpPayRequest → POST RefId to the payment
/// page → callback (identity-validated in the use case) → bpVerifyRequest →
/// bpSettleRequest, with bpInquiryRequest reconciling unknown verify outcomes and
/// bpReversalRequest available for unresolved transactions (≤ ~3h after verify).
///
/// Exactly-once guarantees remain owned by Qasedak's PaymentAttempt persistence; this
/// adapter never treats a callback value as payment proof and never logs credentials.
/// Internal to Infrastructure: only IPaymentGateway/the resolver see it from Application.
/// </summary>
internal sealed class BehpardakhtMellatPaymentGateway(
    IOptions<BehpardakhtOptions> options,
    IBehpardakhtSoapClient soapClient) : IPaymentGateway
{
    public const string ProviderIdValue = "mellat";

    private readonly BehpardakhtOptions _options = options.Value;

    public string ProviderId => ProviderIdValue;

    /// <summary>Vendor contract §8: every payment request needs a unique numeric orderId.</summary>
    internal static long DeriveOrderId(Guid attemptId)
    {
        var bytes = attemptId.ToByteArray();
        var candidate = BitConverter.ToInt64(bytes, 0) & 0x1FFFFFFFFFFFFF; // keep well inside positive long
        return candidate == 0 ? 0x1 : candidate;
    }

    public async Task<PaymentInitialization> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOperational();

        var orderId = DeriveOrderId(request.AttemptId);
        var nowUtc = DateTime.UtcNow;
        // The v1.29 date/time examples are plain Gregorian digit strings.
        var payResult = await soapClient.PayAsync(
            new BehpardakhtPayRequest(
                ParseTerminalId(),
                _options.Username,
                _options.Password,
                orderId,
                request.AmountIrr, // canonical IRR passes through unchanged (vendor operates IRR)
                nowUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                nowUtc.ToString("HHmmss", CultureInfo.InvariantCulture),
                $"qasedak-attempt:{request.AttemptId}",
                request.CallbackUrl,
                "0"), // payerId documented as string; "0" = unspecified
            cancellationToken);

        if (payResult.ResCode != 0)
        {
            throw new PaymentRequestRejectedException(payResult.ResCode, DescribeRejection("pay", payResult.ResCode));
        }

        if (string.IsNullOrEmpty(payResult.RefId))
        {
            // ResCode 0 without a RefId is a protocol violation — refuse rather than guess.
            throw new PaymentGatewayUnavailableException("The Behpardakht pay response omitted the required RefId.");
        }

        // Redirect goes through our own jump endpoint which auto-posts the exact-case
        // RefId to the configured payment page; credentials never reach the browser and
        // the Referer requirement is satisfied by hosting the jump on the registered domain.
        var origin = new Uri(request.CallbackUrl).GetLeftPart(UriPartial.Authority);
        var redirectUrl =
            $"{origin}/api/v1/payments/{ProviderIdValue}/startpay?authority={Uri.EscapeDataString(payResult.RefId)}&attempt={request.AttemptId}";

        return new PaymentInitialization(ProviderIdValue, payResult.RefId, redirectUrl, orderId);
    }

    public async Task<PaymentVerificationResult> VerifyAsync(VerifyPaymentRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOperational();

        if (request.ProviderOrderId is not { } orderId || string.IsNullOrWhiteSpace(request.ProviderVerificationRef))
        {
            // Without the stored order identity or the bank reference there is nothing to verify.
            throw new PaymentRequestRejectedException(null, "Behpardakht verification requires the stored order id and sale reference.");
        }

        if (!long.TryParse(request.ProviderVerificationRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out var saleReferenceId))
        {
            throw new PaymentRequestRejectedException(null, "Behpardakht sale reference is not a valid number.");
        }

        // Verify orderId may equal saleOrderId (§10.3); both identify the same purchase.
        var transaction = BuildTransaction(orderId, saleReferenceId);

        int verifyCode;
        try
        {
            verifyCode = (await soapClient.VerifyAsync(transaction, cancellationToken)).ResCode;
        }
        catch (PaymentGatewayUnavailableException)
        {
            // Verify outcome unknown (timeout/fault/unreachable): reconcile via Inquiry
            // instead of blindly retrying Verify (§7 of the guide / §21.5 engineering rules).
            return await ResolveUnknownOutcomeAsync(transaction, cancellationToken);
        }

        return await ContinueFromVerifyCodeAsync(transaction, verifyCode, wasAlreadyVerified: false, cancellationToken);
    }

    /// <summary>
    /// Reversal for unresolved transactions where service must not be delivered (§13).
    /// Never call after a successful settlement. Exposed for operational reconciliation;
    /// the exactly-once entitlement rules live in the use case, not here.
    /// </summary>
    /// <returns>The vendor ResCode (0 = reversal accepted).</returns>
    public async Task<int> ReverseAsync(long orderId, long saleReferenceId, CancellationToken cancellationToken = default)
    {
        EnsureOperational();
        return (await soapClient.ReverseAsync(BuildTransaction(orderId, saleReferenceId), cancellationToken)).ResCode;
    }

    private async Task<PaymentVerificationResult> ContinueFromVerifyCodeAsync(
        BehpardakhtTransactionRequest transaction, int verifyCode, bool wasAlreadyVerified, CancellationToken cancellationToken)
    {
        switch (BehpardakhtResponseCodes.Classify(verifyCode))
        {
            case BehpardakhtCodeClass.Success:
                break;

            case BehpardakhtCodeClass.IdempotentSuccess:
                // 43: verified earlier while our side stayed Pending — still settle, then
                // report AlreadyVerified so the use case applies entitlement exactly once.
                wasAlreadyVerified = true;
                break;

            case BehpardakhtCodeClass.UserCancelled:
            case BehpardakhtCodeClass.DefinitiveFailure:
            case BehpardakhtCodeClass.ConfigurationError:
                return PaymentVerificationResult.Failed(verifyCode, DescribeRejection("verify", verifyCode));

            case BehpardakhtCodeClass.UnknownState:
            default:
                return await ResolveUnknownOutcomeAsync(transaction, cancellationToken);
        }

        // Settle only after an explicitly successful/idempotent verification (§11).
        int settleCode;
        try
        {
            settleCode = (await soapClient.SettleAsync(transaction, cancellationToken)).ResCode;
        }
        catch (PaymentGatewayUnavailableException exception)
        {
            // Settle is idempotent (45), but its outcome is unknown here — leave Pending
            // for retry instead of inventing success or reversing on a guess.
            throw new PaymentGatewayUnavailableException($"Behpardakht settle outcome unknown ({exception.Message}).");
        }

        return BehpardakhtResponseCodes.Classify(settleCode) switch
        {
            BehpardakhtCodeClass.Success => wasAlreadyVerified
                ? PaymentVerificationResult.AlreadyVerified(verifyCode)
                : PaymentVerificationResult.Verified(verifyCode, transaction.SaleReferenceId.ToString(CultureInfo.InvariantCulture), null, null),
            BehpardakhtCodeClass.IdempotentSuccess => wasAlreadyVerified
                ? PaymentVerificationResult.AlreadyVerified(verifyCode)
                : PaymentVerificationResult.Verified(verifyCode, transaction.SaleReferenceId.ToString(CultureInfo.InvariantCulture), null, null),
            _ => PaymentVerificationResult.Failed(settleCode, DescribeRejection("settle", settleCode)),
        };
    }

    private async Task<PaymentVerificationResult> ResolveUnknownOutcomeAsync(
        BehpardakhtTransactionRequest transaction, CancellationToken cancellationToken)
    {
        int inquiryCode;
        try
        {
            inquiryCode = (await soapClient.InquiryAsync(transaction, cancellationToken)).ResCode;
        }
        catch (PaymentGatewayUnavailableException)
        {
            // Even Inquiry could not establish state. Leave the attempt Pending; reversal
            // remains an explicit operational decision within the documented window.
            throw new PaymentGatewayUnavailableException("Behpardakht inquiry could not establish the transaction state.");
        }

        return BehpardakhtResponseCodes.Classify(inquiryCode) switch
        {
            BehpardakhtCodeClass.Success => await ContinueFromVerifyCodeAsync(transaction, 0, wasAlreadyVerified: false, cancellationToken),
            BehpardakhtCodeClass.IdempotentSuccess => await ContinueFromVerifyCodeAsync(transaction, 43, wasAlreadyVerified: true, cancellationToken),
            BehpardakhtCodeClass.UserCancelled =>
                PaymentVerificationResult.Failed(inquiryCode, DescribeRejection("inquiry", inquiryCode)),
            BehpardakhtCodeClass.DefinitiveFailure =>
                PaymentVerificationResult.Failed(inquiryCode, DescribeRejection("inquiry", inquiryCode)),
            BehpardakhtCodeClass.ConfigurationError =>
                PaymentVerificationResult.Failed(inquiryCode, DescribeRejection("inquiry", inquiryCode)),
            _ => throw new PaymentGatewayUnavailableException("Behpardakht transaction state remains unresolved after inquiry."),
        };
    }

    private BehpardakhtTransactionRequest BuildTransaction(long orderId, long saleReferenceId) =>
        new(ParseTerminalId(), _options.Username, _options.Password, orderId, orderId, saleReferenceId);

    private long ParseTerminalId() =>
        long.TryParse(_options.TerminalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var terminalId)
            ? terminalId
            : throw new PaymentProviderDisabledException(ProviderIdValue);

    private void EnsureOperational()
    {
        if (!_options.Enabled ||
            _options.TerminalId.Length == 0 ||
            _options.Username.Length == 0 ||
            _options.Password.Length == 0 ||
            _options.ServiceUrl.Length == 0)
        {
            throw new PaymentProviderDisabledException(ProviderIdValue);
        }
    }

    /// <summary>Safe rejection text — code class plus code, no credential or payload material.</summary>
    private static string DescribeRejection(string operation, int resCode) =>
        $"Behpardakht {operation} declined with code {resCode} ({BehpardakhtResponseCodes.Describe(resCode)}).";
}

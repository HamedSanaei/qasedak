using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Billing.Application.Payments;
using Qasedak.Modules.Billing.Infrastructure.Payments;
using Xunit;

namespace Qasedak.Modules.Billing.UnitTests;

/// <summary>
/// Contract tests for the Zarinpal adapter against deterministic fixtures of the OFFICIAL
/// gateway REST responses (request.json / verify.json, codes 100/101). No network.
/// </summary>
public sealed class ZarinpalGatewayContractTests
{
    private const string MerchantId = "0123456789abcdef0123456789abcdefabcd";

    [Fact]
    public async Task RequestCode100ReturnsAuthorityAndStartpayRedirect()
    {
        var (gateway, handler) = CreateGateway(handlerResponse: () => Json("""
            {"data":{"code":100,"message":"Success","authority":"A0000000000000000000000000012345","fee_type":"Merchant","fee":2000},"errors":[]}
            """));

        var initialization = await gateway.CreatePaymentAsync(
            new CreatePaymentRequest(Guid.CreateVersion7(), 1_500_000, "Qasedak subscription: pro", "https://api.test/api/v1/payments/callback/zarinpal?attempt=e"),
            CancellationToken.None);

        Assert.Equal("zarinpal", initialization.ProviderId);
        Assert.Equal("A0000000000000000000000000012345", initialization.Authority);
        Assert.Equal("https://payment.zarinpal.com/pg/StartPay/A0000000000000000000000000012345", initialization.RedirectUrl);

        // Official request contract fields must all be present and correct.
        var body = handler.LastRequestJson!;
        Assert.Contains($"\"merchant_id\":\"{MerchantId}\"", body);
        Assert.Contains("\"amount\":1500000", body);
        Assert.Contains("\"currency\":\"IRR\"", body);
        Assert.Contains("\"callback_url\":", body);
        Assert.Contains("\"description\":", body);
    }

    [Fact]
    public async Task RequestRejectionMapsToTypedExceptionWithCode()
    {
        var harness = CreateGateway(handlerResponse: () => Json("""
            {"data":null,"errors":[{"code":-9,"message":"validation error"}]}
            """));

        var rejection = await Assert.ThrowsAsync<PaymentRequestRejectedException>(() =>
            harness.Gateway.CreatePaymentAsync(NewRequest(), CancellationToken.None));

        Assert.DoesNotContain(MerchantId, rejection.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestTransportFailureMapsToUnavailable()
    {
        var harness = CreateGateway(throwOnSend: true);

        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() =>
            harness.Gateway.CreatePaymentAsync(NewRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task RequestTimeoutMapsToUnavailable()
    {
        // A TaskCanceledException NOT caused by the caller token = provider timeout.
        var harness = CreateGateway(throwOnSend: true, throwType: FailureKind.Timeout);

        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() =>
            harness.Gateway.VerifyAsync(new VerifyPaymentRequest("a", 100), CancellationToken.None));
    }

    [Fact]
    public async Task RequestMalformedBodyMapsToUnavailable()
    {
        var harness = CreateGateway(handlerResponse: () => new StringContent("<html>not json</html>", System.Text.Encoding.UTF8, "text/html"), statusCode: 502);

        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() =>
            harness.Gateway.CreatePaymentAsync(NewRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task VerifyCode100YieldsVerifiedWithReferenceAndMaskedCard()
    {
        var harness = CreateGateway(handlerResponse: () => Json("""
            {"data":{"code":100,"message":"Verified","ref_id":852417963,"card_pan":"6037********1234","card_hash":"1E4F2C"},"errors":[]}
            """));

        var result = await harness.Gateway.VerifyAsync(
            new VerifyPaymentRequest("A123", 1_500_000), CancellationToken.None);

        Assert.Equal(PaymentVerificationOutcome.Verified, result.Outcome);
        Assert.Equal(100, result.ProviderCode);
        Assert.Equal("852417963", result.ProviderReferenceId);
        Assert.Equal("6037********1234", result.MaskedCardPan); // masked PAN only — full PAN never returned by the official API
        Assert.Equal("1E4F2C", result.CardHash);

        var verifyBody = harness.Handler.LastRequestJson!;
        Assert.Contains($"\"merchant_id\":\"{MerchantId}\"", verifyBody);
        Assert.Contains("\"amount\":1500000", verifyBody);
        Assert.Contains("\"authority\":\"A123\"", verifyBody);
    }

    [Fact]
    public async Task VerifyCode101YieldsAlreadyVerifiedForIdempotentReplay()
    {
        var harness = CreateGateway(handlerResponse: () => Json("""
            {"data":{"code":101,"message":"verified before"},"errors":[]}
            """));

        var result = await harness.Gateway.VerifyAsync(
            new VerifyPaymentRequest("A123", 1_500_000), CancellationToken.None);

        Assert.Equal(PaymentVerificationOutcome.AlreadyVerified, result.Outcome);
    }

    [Fact]
    public async Task VerifyFailureCodeYieldsFailed()
    {
        var harness = CreateGateway(handlerResponse: () => Json("""
            {"data":{"code":-54,"message":"authority not found"},"errors":[]}
            """));

        var result = await harness.Gateway.VerifyAsync(
            new VerifyPaymentRequest("missing", 1), CancellationToken.None);

        Assert.Equal(PaymentVerificationOutcome.Failed, result.Outcome);
        Assert.Equal(-54, result.ProviderCode);
        // Failure detail must never leak the merchant id into messages or logs surfaces.
        Assert.DoesNotContain(MerchantId, result.ErrorDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledOptionsFailClosedBeforeAnyHttpCall()
    {
        var options = Options.Create(new ZarinpalOptions { Enabled = false, MerchantId = MerchantId });
        using var httpClient = new HttpClient(new FakeHandler());
        var gateway = new ZarinpalPaymentGateway(httpClient, options, NullLogger<ZarinpalPaymentGateway>.Instance);

        await Assert.ThrowsAsync<PaymentProviderDisabledException>(() =>
            gateway.CreatePaymentAsync(NewRequest(), CancellationToken.None));
    }

    private static CreatePaymentRequest NewRequest() =>
        new(Guid.CreateVersion7(), 1_500_000, "desc", "https://api.test/callback");

    private static StringContent Json(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    private static Harness CreateGateway(
        Func<StringContent>? handlerResponse = null,
        int statusCode = 200,
        bool throwOnSend = false,
        FailureKind throwType = FailureKind.Http)
    {
        var handler = new FakeHandler(handlerResponse, statusCode, throwOnSend, throwType);
        var options = Options.Create(new ZarinpalOptions
        {
            Enabled = true,
            MerchantId = MerchantId,
            BaseUrl = "https://payment.zarinpal.com",
            Currency = "IRR",
        });
        var gateway = new ZarinpalPaymentGateway(
            new HttpClient(handler) { BaseAddress = new Uri("https://payment.zarinpal.com/") },
            options,
            NullLogger<ZarinpalPaymentGateway>.Instance);
        return new Harness(gateway, handler);
    }

    private sealed record Harness(ZarinpalPaymentGateway Gateway, FakeHandler Handler);

    /// <summary>Scripted transport failure kinds.</summary>
    internal enum FailureKind { Http, Timeout }

    /// <summary>Deterministic HttpMessageHandler with scripted failures — no live calls.</summary>
    internal sealed class FakeHandler(
        Func<StringContent>? response = null,
        int statusCode = 200,
        bool throwOnSend = false,
        FailureKind throwType = FailureKind.Http) : HttpMessageHandler
    {
        public string? LastRequestJson { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastRequestJson = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (throwOnSend)
            {
                throw throwType == FailureKind.Timeout
                    ? new TaskCanceledException("simulated timeout")
                    : new HttpRequestException("simulated connection failure");
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = response?.Invoke() ?? new StringContent("{\"data\":{\"code\":100},\"errors\":[]}"),
                StatusCode = (System.Net.HttpStatusCode)statusCode,
            };
        }
    }
}

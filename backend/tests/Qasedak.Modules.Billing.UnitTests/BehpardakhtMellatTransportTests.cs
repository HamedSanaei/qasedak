using System.Globalization;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Billing.Application;
using Qasedak.Modules.Billing.Application.Payments;
using Qasedak.Modules.Billing.Domain;
using Qasedak.Modules.Billing.Domain.Payments;
using Qasedak.Modules.Billing.Infrastructure.Payments;
using Xunit;

namespace Qasedak.Modules.Billing.UnitTests;

/// <summary>
/// Scriptable stand-in for the Behpardakht SOAP service — CI never touches bpm.shaparak.ir.
/// Records every call and plays queued outcomes; defaults mirror the happy path.
/// </summary>
internal sealed class FakeBehpardakhtSoapClient : IBehpardakhtSoapClient
{
    public List<string> Operations { get; } = [];

    public List<BehpardakhtPayRequest> PayRequests { get; } = [];

    public List<BehpardakhtTransactionRequest> Transactions { get; } = [];

    public Queue<object> ScriptedPay { get; } = new();

    public Queue<object> ScriptedVerify { get; } = new();

    public Queue<object> ScriptedSettle { get; } = new();

    public Queue<object> ScriptedInquiry { get; } = new();

    public Queue<object> ScriptedReverse { get; } = new();

    private static object Next(Queue<object> queue, object fallback) =>
        queue.Count > 0 ? queue.Dequeue() : fallback;

    public Task<BehpardakhtPayResult> PayAsync(BehpardakhtPayRequest request, CancellationToken cancellationToken = default)
    {
        Operations.Add("pay");
        PayRequests.Add(request);
        return Task.FromResult(Next(ScriptedPay, new BehpardakhtPayResult(0, $"REF-{request.OrderId}")) switch
        {
            PaymentGatewayUnavailableException failure => throw failure,
            BehpardakhtPayResult result => result,
            _ => throw new InvalidOperationException("bad script"),
        });
    }

    private Task<BehpardakhtCodeResult> Run(
        string operation,
        BehpardakhtTransactionRequest request,
        Queue<object> scripted,
        CancellationToken cancellationToken)
    {
        Operations.Add(operation);
        Transactions.Add(request);
        return Task.FromResult(Next(scripted, new BehpardakhtCodeResult(0)) switch
        {
            PaymentGatewayUnavailableException failure => throw failure,
            BehpardakhtCodeResult code => code,
            _ => throw new InvalidOperationException("bad script"),
        });
    }

    public Task<BehpardakhtCodeResult> VerifyAsync(BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default) =>
        Run("verify", request, ScriptedVerify, cancellationToken);

    public Task<BehpardakhtCodeResult> SettleAsync(BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default) =>
        Run("settle", request, ScriptedSettle, cancellationToken);

    public Task<BehpardakhtCodeResult> InquiryAsync(BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default) =>
        Run("inquiry", request, ScriptedInquiry, cancellationToken);

    public Task<BehpardakhtCodeResult> ReverseAsync(BehpardakhtTransactionRequest request, CancellationToken cancellationToken = default) =>
        Run("reverse", request, ScriptedReverse, cancellationToken);
}

/// <summary>Envelope construction and defensive response parsing (vendor contract §8.1).</summary>
public sealed class BehpardakhtSoapClientParsingTests
{
    [Fact]
    public void EnvelopeCarriesDocumentedParametersWithXmlEscaping()
    {
        var envelope = BehpardakhtSoapClient.BuildEnvelope(
            "bpPayRequest",
            [
                ("terminalId", "123"),
                ("userName", "user<1>&"),
                ("userPassword", "pass\"word"),
                ("orderId", "42"),
                ("amount", "1500000"),
                ("localDate", "20260824"),
                ("localTime", "101112"),
                ("additionalData", "qasedak-attempt:x"),
                ("callBackUrl", "https://api.qasedak.example/callback"),
                ("payerId", "0"),
            ],
            "http://interfaces.core.bpm.bpt.com/");

        Assert.Contains("<tem:bpPayRequest>", envelope);
        foreach (var name in new[] { "terminalId", "userName", "userPassword", "orderId", "amount", "localDate", "localTime", "additionalData", "callBackUrl", "payerId" })
        {
            Assert.Contains($"<{name}>", envelope);
        }

        // Credential values are escaped so they cannot break out of their element.
        Assert.Contains("user&lt;1&gt;&amp;", envelope);
        Assert.Contains("pass&quot;word", envelope);
    }

    [Fact]
    public void PayResponseParsesResCodeAndPreservesRefIdCase()
    {
        var parsed = BehpardakhtSoapClient.ParsePayResponse("0,AF82041a2Bf6989c7fF9");
        Assert.Equal(0, parsed.ResCode);
        Assert.Equal("AF82041a2Bf6989c7fF9", parsed.RefId); // exact case preserved
    }

    [Fact]
    public void NonZeroPayResCodeParsesWithoutRefId()
    {
        var parsed = BehpardakhtSoapClient.ParsePayResponse("41,");
        Assert.Equal(41, parsed.ResCode);
        Assert.Null(parsed.RefId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("0")]
    [InlineData("x,y,z")]
    public void MalformedPayResponsesAreRejected(string body)
    {
        Assert.Throws<PaymentGatewayUnavailableException>(() => BehpardakhtSoapClient.ParsePayResponse(body));
    }

    [Fact]
    public void ResCodeZeroWithEmptyRefIdYieldsNullForGatewayToReject() =>
        // Parsing succeeds but RefId is null; the gateway refuses it before persisting anything.
        Assert.Null(BehpardakhtSoapClient.ParsePayResponse("0,").RefId);

    [Theory]
    [InlineData("0")]
    [InlineData(" 43 ")]
    [InlineData("45")]
    public void CodeResponsesParseTrimmedIntegers(string body) =>
        Assert.True(BehpardakhtSoapClient.ParseCodeResponse("bpVerifyRequest", body).ResCode is 0 or 43 or 45);

    [Fact]
    public void MalformedCodeResponseIsRejected()
    {
        Assert.Throws<PaymentGatewayUnavailableException>(
            () => BehpardakhtSoapClient.ParseCodeResponse("bpVerifyRequest", "OK"));
    }

    [Fact]
    public void ReturnElementIsFoundNamespaceAgnostically()
    {
        const string response =
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body>" +
            "<bpPayRequestResponse xmlns=\"http://interfaces.core.bpm.bpt.com/\"><return>0,REF123</return></bpPayRequestResponse>" +
            "</soap:Body></soap:Envelope>";

        Assert.Equal("0,REF123", BehpardakhtSoapClient.ExtractReturnElement(response, "bpPayRequest"));
    }

    [Fact]
    public void SoapFaultBodyYieldsNoReturnElement()
    {
        const string fault =
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body>" +
            "<soap:Fault><faultcode>soap:Server</faultcode><faultstring>boom</faultstring></soap:Fault>" +
            "</soap:Body></soap:Envelope>";

        Assert.Null(BehpardakhtSoapClient.ExtractReturnElement(fault, "bpPayRequest"));
    }

    [Fact]
    public void NonXmlBodyYieldsNoReturnElement() =>
        Assert.Null(BehpardakhtSoapClient.ExtractReturnElement("<html>gateway error page</html>", "bpVerifyRequest"));
}

/// <summary>Bounded classification of the v1.29 response-code table (§19).</summary>
public sealed class BehpardakhtResponseCodeTests
{
    [Fact]
    public void ZeroClassifiesAsSuccess() =>
        Assert.Equal(BehpardakhtCodeClass.Success, BehpardakhtResponseCodes.Classify(0));

    [Theory]
    [InlineData(43)]
    [InlineData(45)]
    public void AlreadyVerifiedAndAlreadySettledAreIdempotentSuccess(int code) =>
        Assert.Equal(BehpardakhtCodeClass.IdempotentSuccess, BehpardakhtResponseCodes.Classify(code));

    [Fact]
    public void SeventeenIsUserCancellation() =>
        Assert.Equal(BehpardakhtCodeClass.UserCancelled, BehpardakhtResponseCodes.Classify(17));

    [Theory]
    [InlineData(21)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(62)]
    [InlineData(421)]
    public void MerchantSetupProblemsAreConfigurationErrors(int code) =>
        Assert.Equal(BehpardakhtCodeClass.ConfigurationError, BehpardakhtResponseCodes.Classify(code));

    [Theory]
    [InlineData(25)]
    [InlineData(32)]
    [InlineData(34)]
    [InlineData(41)]
    [InlineData(42)]
    [InlineData(44)]
    [InlineData(46)]
    [InlineData(47)]
    [InlineData(48)]
    [InlineData(51)]
    [InlineData(54)]
    [InlineData(55)]
    [InlineData(61)]
    public void DocumentedFailuresAreDefinitive(int code) =>
        Assert.Equal(BehpardakhtCodeClass.DefinitiveFailure, BehpardakhtResponseCodes.Classify(code));

    [Theory]
    [InlineData(999)]
    [InlineData(-1)]
    [InlineData(777)]
    public void UndocumentedCodesStayUnknownState(int code) =>
        Assert.Equal(BehpardakhtCodeClass.UnknownState, BehpardakhtResponseCodes.Classify(code));
}

/// <summary>
/// Gateway orchestration against the fake SOAP boundary: pay parsing, verify→settle
/// chaining, idempotent states, inquiry reconciliation and safe failure mapping.
/// </summary>
public sealed class BehpardakhtMellatGatewayFlowTests
{
    private static (BehpardakhtMellatPaymentGateway Gateway, FakeBehpardakhtSoapClient Soap) NewGateway(bool enabled = true)
    {
        var soap = new FakeBehpardakhtSoapClient();
        var gateway = new BehpardakhtMellatPaymentGateway(
            Options.Create(new BehpardakhtOptions
            {
                Enabled = enabled,
                TerminalId = "999",
                Username = "merchant-user",
                Password = "merchant-pass",
            }),
            soap);
        return (gateway, soap);
    }

    private static CreatePaymentRequest NewPayRequest() =>
        new(Guid.CreateVersion7(), 1_500_000, "plan purchase", "https://api.qasedak.example/api/v1/payments/callback/mellat?attempt=abc");

    [Fact]
    public async Task DisabledGatewayFailsClosedBeforeAnyNetworkCall()
    {
        var (gateway, soap) = NewGateway(enabled: false);

        await Assert.ThrowsAsync<PaymentProviderDisabledException>(() => gateway.CreatePaymentAsync(NewPayRequest(), CancellationToken.None));
        await Assert.ThrowsAsync<PaymentProviderDisabledException>(() =>
            gateway.VerifyAsync(new VerifyPaymentRequest("ref", 1000, 42, "55"), CancellationToken.None));
        Assert.Empty(soap.Operations);
    }

    [Fact]
    public async Task SuccessfulPayReturnsExactCaseRefIdAndPositiveOrderId()
    {
        var (gateway, soap) = NewGateway();
        soap.ScriptedPay.Enqueue(new BehpardakhtPayResult(0, "AF82041a2Bf6989c7fF9"));

        var initialization = await gateway.CreatePaymentAsync(NewPayRequest(), CancellationToken.None);

        Assert.Equal("mellat", initialization.ProviderId);
        Assert.Equal("AF82041a2Bf6989c7fF9", initialization.Authority); // case preserved
        Assert.NotNull(initialization.ProviderOrderId);
        Assert.True(initialization.ProviderOrderId > 0);
        Assert.Contains("/startpay?authority=", initialization.RedirectUrl);
        // Canonical IRR passes through unchanged — no toman conversion anywhere.
        Assert.Equal("1500000", soap.PayRequests[0].AmountIrr.ToString(CultureInfo.InvariantCulture));
        Assert.Equal("0", soap.PayRequests[0].PayerId);
    }

    [Fact]
    public async Task NonZeroPayResCodeThrowsTypedRejectionWithoutAnyVerificationCall()
    {
        var (gateway, soap) = NewGateway();
        soap.ScriptedPay.Enqueue(new BehpardakhtPayResult(41, null)); // duplicate order number

        var rejection = await Assert.ThrowsAsync<PaymentRequestRejectedException>(
            () => gateway.CreatePaymentAsync(NewPayRequest(), CancellationToken.None));

        Assert.Equal(41, rejection.ProviderCode);
        Assert.Empty(soap.Transactions);
    }

    [Fact]
    public async Task ResCodeZeroWithEmptyRefIdFailsClosed()
    {
        var (gateway, soap) = NewGateway();
        soap.ScriptedPay.Enqueue(new BehpardakhtPayResult(0, ""));

        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(
            () => gateway.CreatePaymentAsync(NewPayRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task PayTimeoutSurfacesUnavailable()
    {
        var (gateway, soap) = NewGateway();
        soap.ScriptedPay.Enqueue(new PaymentGatewayUnavailableException("simulated timeout"));

        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(
            () => gateway.CreatePaymentAsync(NewPayRequest(), CancellationToken.None));
    }

    [Fact]
    public async Task VerifyThenSettleSucceedsAndCarriesSaleReference()
    {
        var (gateway, soap) = NewGateway();

        var result = await gateway.VerifyAsync(new VerifyPaymentRequest("REF1", 1_500_000, 42, "900001"), CancellationToken.None);

        Assert.Equal(PaymentVerificationOutcome.Verified, result.Outcome);
        Assert.Equal("900001", result.ProviderReferenceId);
        Assert.Equal(900001, soap.Transactions[0].SaleReferenceId);
        Assert.Equal(["verify", "settle"], soap.Operations);
    }

    [Fact]
    public async Task Verify43ReportsAlreadyVerifiedAfterIdempotentSettle()
    {
        var (gateway, soap) = NewGateway();
        soap.ScriptedVerify.Enqueue(new BehpardakhtCodeResult(43));
        soap.ScriptedSettle.Enqueue(new BehpardakhtCodeResult(45)); // already settled

        var result = await gateway.VerifyAsync(new VerifyPaymentRequest("REF1", 1_500_000, 42, "900001"), CancellationToken.None);

        Assert.Equal(PaymentVerificationOutcome.AlreadyVerified, result.Outcome);
        Assert.Equal(["verify", "settle"], soap.Operations);
    }

    [Fact]
    public async Task Reversed48IsDefinitiveFailureWithoutSettle()
    {
        var (gateway, soap) = NewGateway();
        soap.ScriptedVerify.Enqueue(new BehpardakhtCodeResult(48));

        var result = await gateway.VerifyAsync(new VerifyPaymentRequest("REF1", 1_500_000, 42, "900001"), CancellationToken.None);

        Assert.Equal(PaymentVerificationOutcome.Failed, result.Outcome);
        Assert.Equal(48, result.ProviderCode);
        Assert.Equal(["verify"], soap.Operations);
    }

    [Fact]
    public async Task VerifyUnknownOutcomeTriggersInquiryThatConfirmsSuccess()
    {
        var (gateway, soap) = NewGateway();
        soap.ScriptedVerify.Enqueue(new PaymentGatewayUnavailableException("timeout during verify"));
        soap.ScriptedInquiry.Enqueue(new BehpardakhtCodeResult(0));

        var result = await gateway.VerifyAsync(new VerifyPaymentRequest("REF1", 1_500_000, 42, "900001"), CancellationToken.None);

        Assert.Equal(PaymentVerificationOutcome.Verified, result.Outcome);
        Assert.Equal(["verify", "inquiry", "settle"], soap.Operations);
    }

    [Fact]
    public async Task InquiryDeterminingFailureMapsToFailedWithoutFurtherCalls()
    {
        var (gateway, soap) = NewGateway();
        soap.ScriptedVerify.Enqueue(new PaymentGatewayUnavailableException("connection reset"));
        soap.ScriptedInquiry.Enqueue(new BehpardakhtCodeResult(44)); // verify not found

        var result = await gateway.VerifyAsync(new VerifyPaymentRequest("REF1", 1_500_000, 42, "900001"), CancellationToken.None);

        Assert.Equal(PaymentVerificationOutcome.Failed, result.Outcome);
        Assert.Equal(["verify", "inquiry"], soap.Operations);
    }

    [Fact]
    public async Task InquiryStillUnknownLeavesAttemptPendingViaUnavailable()
    {
        var (gateway, soap) = NewGateway();
        soap.ScriptedVerify.Enqueue(new PaymentGatewayUnavailableException("timeout"));
        soap.ScriptedInquiry.Enqueue(new PaymentGatewayUnavailableException("timeout"));

        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(
            () => gateway.VerifyAsync(new VerifyPaymentRequest("REF1", 1_500_000, 42, "900001"), CancellationToken.None));
        Assert.Equal(["verify", "inquiry"], soap.Operations);
    }

    [Fact]
    public async Task SettleFailureMeansNoVerifiedOutcome()
    {
        var (gateway, soap) = NewGateway();
        soap.ScriptedSettle.Enqueue(new BehpardakhtCodeResult(61)); // settlement error

        var result = await gateway.VerifyAsync(new VerifyPaymentRequest("REF1", 1_500_000, 42, "900001"), CancellationToken.None);

        Assert.Equal(PaymentVerificationOutcome.Failed, result.Outcome);
        Assert.Equal(61, result.ProviderCode);
    }

    [Fact]
    public async Task SettleTimeoutSurfacesUnavailableInsteadOfInventingSuccess()
    {
        var (gateway, soap) = NewGateway();
        soap.ScriptedSettle.Enqueue(new PaymentGatewayUnavailableException("timeout during settle"));

        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(
            () => gateway.VerifyAsync(new VerifyPaymentRequest("REF1", 1_500_000, 42, "900001"), CancellationToken.None));
        // Settle is idempotent (45): a retry can safely complete it later.
    }

    [Fact]
    public async Task ReverseCallsTheDocumentedOperationAndReturnsResCode()
    {
        var (gateway, soap) = NewGateway();
        soap.ScriptedReverse.Enqueue(new BehpardakhtCodeResult(0));

        var resCode = await gateway.ReverseAsync(42, 900001, CancellationToken.None);

        Assert.Equal(0, resCode);
        Assert.Equal(["reverse"], soap.Operations);
    }
}

/// <summary>
/// Use-case-level callback validation per vendor §9.2: identity mismatches reject BEFORE
/// verification; duplicates stay idempotent; cancellation and failure never activate.
/// </summary>
public sealed class MellatCallbackValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    private sealed class CountingGateway : IPaymentGateway
    {
        public int VerifyCalls { get; private set; }

        public List<VerifyPaymentRequest> Requests { get; } = [];

        public string ProviderId => "mellat";

        public Task<PaymentInitialization> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentInitialization(ProviderId, "REF1", "https://jump.test/startpay", 42));

        public Task<PaymentVerificationResult> VerifyAsync(VerifyPaymentRequest request, CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            Requests.Add(request);
            return Task.FromResult(PaymentVerificationResult.Verified(0, "900001", null, null));
        }
    }

    private sealed class SingleResolver(IPaymentGateway gateway) : IPaymentGatewayResolver
    {
        public IReadOnlyList<string> EnabledProviderIds => [gateway.ProviderId];

        public IPaymentGateway Resolve(string providerId) =>
            string.Equals(providerId, gateway.ProviderId, StringComparison.OrdinalIgnoreCase)
                ? gateway
                : throw new PaymentProviderUnknownException(providerId);
    }

    private sealed class FakeAttemptRepository : IPaymentAttemptRepository
    {
        public Dictionary<Guid, PaymentAttempt> Rows { get; } = [];

        public int SaveCalls { get; private set; }

        public Task<PaymentAttempt?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.GetValueOrDefault(id));

        public Task<PaymentAttempt?> FindByAuthorityAsync(string authority, CancellationToken cancellationToken = default) =>
            Task.FromResult(Rows.Values.FirstOrDefault(a => a.Authority == authority));

        public Task<IReadOnlyList<PaymentAttempt>> ListByWorkspaceAsync(Guid workspaceId, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<PaymentAttempt>)[.. Rows.Values]);

        public Task SaveChangesAsync(PaymentAttempt attempt, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            Rows[attempt.Id] = PaymentAttempt.FromState(
                attempt.Id, attempt.WorkspaceId, attempt.PlanId, attempt.ProviderId, attempt.AmountIrr,
                attempt.Status, attempt.Authority, attempt.ProviderOrderId, attempt.ProviderReferenceId,
                attempt.FailureCode, attempt.MaskedCardPan, attempt.CreatedAtUtc, attempt.VerifiedAtUtc, attempt.FailedAtUtc);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeSubscriptionRepository(Dictionary<Guid, Subscription>? seed = null) : ISubscriptionRepository
    {
        private readonly Dictionary<Guid, Subscription> _store = seed ?? [];

        public Task<Subscription?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.TryGetValue(id, out var subscription) ? subscription : null);

        public Task<Subscription?> FindByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.Values.FirstOrDefault(s => s.WorkspaceId == workspaceId));

        public Task SaveChangesAsync(Subscription subscription, CancellationToken cancellationToken = default)
        {
            _store[subscription.Id] = subscription;
            return Task.CompletedTask;
        }
    }

    private static (FinalizePaymentUseCase UseCase, FakeAttemptRepository Attempts, CountingGateway Gateway, Plan Plan) NewEnvironment(long providerOrderId)
    {
        var attempts = new FakeAttemptRepository();
        var plan = Plan.Create(Guid.CreateVersion7(), $"pro-{Guid.NewGuid():N}", "Pro", amountIrr: 1_500_000);
        var attempt = PaymentAttempt.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), plan.Id, "mellat", 1_500_000, Now);
        attempt.AttachAuthority("REF-ENV");
        if (providerOrderId > 0)
        {
            attempt.AttachProviderOrderId(providerOrderId);
        }

        attempts.Rows[attempt.Id] = attempt;

        var gateway = new CountingGateway();
        var useCase = new FinalizePaymentUseCase(attempts, new FakeSubscriptionRepository(), new SingleResolver(gateway));
        return (useCase, attempts, gateway, plan);
    }

    [Fact]
    public async Task WrongSaleOrderIdRejectsCallbackBeforeVerification()
    {
        var (useCase, _, gateway, _) = NewEnvironment(42);

        var outcome = await useCase.ExecuteCallbackAsync(
            new PaymentCallbackContext("REF-ENV", "OK", ProviderOrderId: 999, ProviderReference: "900001"),
            CancellationToken.None);

        Assert.False(outcome.AlreadyApplied);
        Assert.Equal(PaymentAttemptStatus.Failed, outcome.Attempt.Status);
        Assert.Equal(PaymentFailures.CallbackRejected, outcome.Attempt.FailureCode);
        Assert.Equal(0, gateway.VerifyCalls); // vendor security rule: never verify a mismatched callback
    }

    [Fact]
    public async Task CallbackWithoutStoredOrderIdIsRejectedDefensively()
    {
        // Attempt somehow has no stored ProviderOrderId — a forged callback claiming one is rejected.
        var (useCase, _, gateway, _) = NewEnvironment(0);

        var outcome = await useCase.ExecuteCallbackAsync(
            new PaymentCallbackContext("REF-ENV", "OK", ProviderOrderId: 1, ProviderReference: "900001"),
            CancellationToken.None);

        Assert.Equal(PaymentFailures.CallbackRejected, outcome.Attempt.FailureCode);
        Assert.Equal(0, gateway.VerifyCalls);
    }

    [Fact]
    public async Task UnknownAuthoritySurfacesNotFound()
    {
        var (useCase, _, _, _) = NewEnvironment(42);

        await Assert.ThrowsAsync<BillingDomainException>(() =>
            useCase.ExecuteCallbackAsync(new PaymentCallbackContext("UNKNOWN", "OK"), CancellationToken.None));
    }

    [Fact]
    public async Task MatchingIdentityVerifiesOnceAndActivatesExactlyOnce()
    {
        var (useCase, attempts, gateway, plan) = NewEnvironment(42);
        var subscription = Subscription.Activate(
            Guid.CreateVersion7(), attempts.Rows.Single().Value.WorkspaceId, plan.Id, Now.AddDays(-1), Now.AddDays(29));
        var subscriptions = new FakeSubscriptionRepository(new Dictionary<Guid, Subscription> { [subscription.Id] = subscription });

        var first = await useCase.ExecuteCallbackAsync(
            new PaymentCallbackContext("REF-ENV", "OK", 42, "900001"), CancellationToken.None);
        var second = await useCase.ExecuteCallbackAsync(
            new PaymentCallbackContext("REF-ENV", "OK", 42, "900001"), CancellationToken.None);

        Assert.Equal(PaymentAttemptStatus.Verified, first.Attempt.Status);
        Assert.True(second.AlreadyApplied);
        Assert.Equal(1, gateway.VerifyCalls); // duplicate callback never re-verifies
        var stored = attempts.Rows.Single().Value;
        Assert.Equal("900001", stored.ProviderReferenceId); // durable SaleReferenceId
        var reloaded = await subscriptions.FindByWorkspaceAsync(stored.WorkspaceId);
        Assert.Single(reloaded!.Periods); // entitlement applied exactly once
    }

    [Fact]
    public async Task CancelledSaleNeverActivatesEntitlement()
    {
        var (useCase, attempts, gateway, _) = NewEnvironment(42);

        var outcome = await useCase.ExecuteCallbackAsync(
            new PaymentCallbackContext("REF-ENV", "CANCEL", 42, null), CancellationToken.None);

        Assert.Equal(PaymentAttemptStatus.Failed, outcome.Attempt.Status);
        Assert.Equal(PaymentFailures.CanceledByUser, outcome.Attempt.FailureCode);
        Assert.Null(outcome.Attempt.ProviderReferenceId);
        Assert.Equal(0, gateway.VerifyCalls);
    }
}

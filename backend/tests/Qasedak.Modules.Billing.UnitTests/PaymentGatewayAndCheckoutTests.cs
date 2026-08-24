using Microsoft.Extensions.Options;
using Qasedak.Modules.Billing.Application;
using Qasedak.Modules.Billing.Application.Payments;
using Qasedak.Modules.Billing.Domain;
using Qasedak.Modules.Billing.Domain.Payments;
using Qasedak.Modules.Billing.Infrastructure.Payments;
using Xunit;

namespace Qasedak.Modules.Billing.UnitTests;

/// <summary>Provider selection fails closed and honors enabled flags.</summary>
public sealed class PaymentGatewayResolverTests
{
    [Fact]
    public void ResolveReturnsZarinpalWhenEnabled()
    {
        var resolver = CreateResolver(zarinpalEnabled: true, mellatEnabled: false);

        Assert.Equal("zarinpal", resolver.Resolve("zarinpal").ProviderId);
        Assert.Equal(["zarinpal"], resolver.EnabledProviderIds);
    }

    [Fact]
    public void ResolveDisabledProviderFailsClosedWithTypedSignal()
    {
        var resolver = CreateResolver(zarinpalEnabled: false, mellatEnabled: false);

        Assert.Throws<PaymentProviderDisabledException>(() => resolver.Resolve("zarinpal"));
        Assert.Throws<PaymentProviderDisabledException>(() => resolver.Resolve("mellat"));
        Assert.Empty(resolver.EnabledProviderIds);
    }

    [Fact]
    public void ResolveUnknownProviderIsRejected()
    {
        var resolver = CreateResolver(zarinpalEnabled: true, mellatEnabled: false);

        // The cancelled Bank Melli/SADAD provider must now be unknown, not disabled.
        Assert.Throws<PaymentProviderUnknownException>(() => resolver.Resolve("melli"));
        Assert.Throws<PaymentProviderUnknownException>(() => resolver.Resolve("paypal"));
    }

    [Fact]
    public async Task MellatBoundaryRefusesOperationEvenWhenFlagFlipped()
    {
        // The CURRENT official Behpardakht contract is absent; an operator enabling the
        // flag must still get a loud refusal instead of a guessed wire format.
        var gateway = new BehpardakhtMellatPaymentGateway(Options.Create(new BehpardakhtOptions
        {
            Enabled = true,
            TerminalId = "t",
            Username = "u",
            Password = "p",
        }));

        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() =>
            gateway.CreatePaymentAsync(
                new CreatePaymentRequest(Guid.CreateVersion7(), 1000, "d", "cb"),
                CancellationToken.None));
        await Assert.ThrowsAsync<PaymentGatewayUnavailableException>(() =>
            gateway.VerifyAsync(
                new VerifyPaymentRequest("a", 1000),
                CancellationToken.None));
    }

    private static PaymentGatewayResolver CreateResolver(bool zarinpalEnabled, bool mellatEnabled)
    {
        var zarinpalOptions = Options.Create(new ZarinpalOptions { Enabled = zarinpalEnabled, MerchantId = "m" });
        var mellatOptions = Options.Create(new BehpardakhtOptions { Enabled = mellatEnabled });
        using var httpClient = new HttpClient();
        return new PaymentGatewayResolver(
            new ZarinpalPaymentGateway(httpClient, zarinpalOptions, Microsoft.Extensions.Logging.Abstractions.NullLogger<ZarinpalPaymentGateway>.Instance),
            new BehpardakhtMellatPaymentGateway(mellatOptions),
            zarinpalOptions,
            mellatOptions);
    }
}

/// <summary>Checkout copies the server-owned plan price into the attempt; clients cannot set amounts.</summary>
public sealed class CreateCheckoutUseCaseTests
{
    [Fact]
    public async Task CheckoutUsesPlanPriceAndReturnsRedirect()
    {
        var plans = new FakePlanRepository(Plan.Create(
            Guid.CreateVersion7(), "pro", "Pro", amountIrr: 1_800_000));
        var attempts = new FakeAttempts();
        var gateway = new RecordingOnlyGateway();
        var useCase = new CreateCheckoutUseCase(
            plans,
            attempts,
            new SingleResolver(gateway),
            audit: null);

        var result = await useCase.ExecuteAsync(Guid.CreateVersion7(), "PRO", "zarinpal", "https://api.test/cb?attempt={attemptId}");

        var attempt = await attempts.FindByIdAsync(result.AttemptId);
        Assert.NotNull(attempt);
        Assert.Equal(1_800_000, attempt!.AmountIrr); // server-authoritative
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.NotNull(attempt.Authority);
        Assert.StartsWith("https://pay.test/", result.RedirectUrl);
        Assert.Contains($"attempt={attempt.Id}", gateway.LastCallbackUrl);
    }

    [Fact]
    public async Task CheckoutRejectsNonpurchasableAndUnknownPlans()
    {
        var freePlan = Plan.Create(Guid.CreateVersion7(), "free", "Free"); // price 0
        var useCase = NewUseCase(plans: [freePlan]);

        await Assert.ThrowsAsync<BillingDomainException>(
            () => useCase.ExecuteAsync(Guid.CreateVersion7(), "free", "zarinpal", "cb"));
        await Assert.ThrowsAsync<BillingDomainException>(
            () => useCase.ExecuteAsync(Guid.CreateVersion7(), "ghost", "zarinpal", "cb"));
    }

    [Fact]
    public async Task CheckoutWithUnknownProviderMapsToStableCode()
    {
        var paid = Plan.Create(Guid.CreateVersion7(), "pro", "Pro", amountIrr: 1000);
        var useCase = NewUseCase(plans: [paid]);

        var exception = await Assert.ThrowsAsync<BillingDomainException>(
            () => useCase.ExecuteAsync(Guid.CreateVersion7(), "pro", "stripe", "cb"));

        Assert.Equal("payment.providerUnknown", exception.RuleCode);
    }

    private static CreateCheckoutUseCase NewUseCase(Plan[] plans) =>
        new(new FakePlanRepository(plans), new FakeAttempts(), new SingleResolver(new RecordingOnlyGateway()), audit: null);

    private sealed class SingleResolver(IPaymentGateway gateway) : IPaymentGatewayResolver
    {
        public IReadOnlyList<string> EnabledProviderIds => [gateway.ProviderId];

        public IPaymentGateway Resolve(string providerId) =>
            string.Equals(providerId, gateway.ProviderId, StringComparison.OrdinalIgnoreCase)
                ? gateway
                : throw new PaymentProviderUnknownException(providerId);
    }

    private sealed class RecordingOnlyGateway : IPaymentGateway
    {
        public string? LastCallbackUrl { get; private set; }

        public string ProviderId => "zarinpal";

        public Task<PaymentInitialization> CreatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
        {
            LastCallbackUrl = request.CallbackUrl;
            return Task.FromResult(new PaymentInitialization(
                "zarinpal", $"auth-{request.AttemptId:N}", $"https://pay.test/{request.AttemptId}"));
        }

        public Task<PaymentVerificationResult> VerifyAsync(VerifyPaymentRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(PaymentVerificationResult.Failed(-1, "not used here"));
    }

    private sealed class FakePlanRepository(params Plan[] seed) : IPlanRepository
    {
        private readonly Dictionary<string, Plan> _byCode = seed.ToDictionary(p => p.Code, p => p);

        public Task<Plan?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byCode.Values.FirstOrDefault(p => p.Id == id));

        public Task<Plan?> FindByCodeAsync(string code, CancellationToken cancellationToken = default) =>
            Task.FromResult(_byCode.TryGetValue(code.Trim().ToLowerInvariant(), out var plan) ? plan : null);

        public Task<IReadOnlyList<Plan>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<Plan>)_byCode.Values.ToList());

        public Task SaveChangesAsync(Plan plan, CancellationToken cancellationToken = default)
        {
            _byCode[plan.Code] = plan;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAttempts : IPaymentAttemptRepository
    {
        private readonly Dictionary<Guid, Domain.Payments.PaymentAttempt> _store = [];

        public Task<Domain.Payments.PaymentAttempt?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.TryGetValue(id, out var attempt) ? attempt : null);

        public Task<Domain.Payments.PaymentAttempt?> FindByAuthorityAsync(string authority, CancellationToken cancellationToken = default) =>
            Task.FromResult(_store.Values.FirstOrDefault(a => a.Authority == authority));

        public Task<IReadOnlyList<Domain.Payments.PaymentAttempt>> ListByWorkspaceAsync(Guid workspaceId, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult((IReadOnlyList<Domain.Payments.PaymentAttempt>)_store.Values.ToList());

        public Task SaveChangesAsync(Domain.Payments.PaymentAttempt attempt, CancellationToken cancellationToken = default)
        {
            _store[attempt.Id] = attempt;
            return Task.CompletedTask;
        }
    }
}

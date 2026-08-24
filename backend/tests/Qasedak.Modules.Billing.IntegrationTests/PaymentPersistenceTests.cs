using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Billing.Application.Payments;
using Qasedak.Modules.Billing.Domain;
using Qasedak.Modules.Billing.Domain.Payments;
using Qasedak.Modules.Billing.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Billing.IntegrationTests;

/// <summary>
/// Payment persistence over real PostgreSQL: attempt round-trips, the unique authority
/// index (anti-replay), optimistic-concurrency finalization (exactly-once entitlement),
/// and server-owned plan pricing.
/// </summary>
[Collection(PostgresTestEnvironment.Name)]
public sealed class PaymentPersistenceTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AttemptRoundTripsThroughEveryField()
    {
        var workspaceId = Guid.CreateVersion7();
        var planId = await SeedPlanAsync("roundtrip-pro", 1_250_000);

        var attempt = PaymentAttempt.Create(Guid.CreateVersion7(), workspaceId, planId, "zarinpal", 1_250_000, Now);
        attempt.AttachAuthority($"auth-{attempt.Id:N}");
        await NewRepository().SaveChangesAsync(attempt);

        attempt.MarkVerified("ref-777", "6037********4321", Now.AddMinutes(3));
        await NewRepository().SaveChangesAsync(attempt);

        var reloaded = await NewRepository().FindByIdAsync(attempt.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(PaymentAttemptStatus.Verified, reloaded!.Status);
        Assert.Equal("ref-777", reloaded.ProviderReferenceId);
        Assert.Equal("6037********4321", reloaded.MaskedCardPan);
        Assert.Equal(1_250_000, reloaded.AmountIrr);
        Assert.Equal(planId, reloaded.PlanId);
    }

    [Fact]
    public async Task AuthorityIsUniqueAcrossAttempts()
    {
        var planId = await SeedPlanAsync("unique-auth-plan", 500_000);
        var first = PaymentAttempt.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), planId, "zarinpal", 500_000, Now);
        first.AttachAuthority("shared-authority-value");
        await NewRepository().SaveChangesAsync(first);

        // A replayed/counterfeit authority must be rejected by the database itself.
        var second = PaymentAttempt.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), planId, "zarinpal", 500_000, Now);
        second.AttachAuthority("shared-authority-value");

        await Assert.ThrowsAnyAsync<Exception>(() => NewRepository().SaveChangesAsync(second));
    }

    [Fact]
    public async Task FindByAuthorityResolvesExactlyOneAttempt()
    {
        var planId = await SeedPlanAsync("lookup-plan", 900_000);
        var attempt = PaymentAttempt.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), planId, "zarinpal", 900_000, Now);
        attempt.AttachAuthority($"auth-lookup-{Guid.NewGuid():N}");
        await NewRepository().SaveChangesAsync(attempt);

        var resolved = await NewRepository().FindByAuthorityAsync(attempt.Authority!);
        Assert.Equal(attempt.Id, resolved!.Id);
        Assert.Null(await NewRepository().FindByAuthorityAsync("missing-authority"));
    }

    [Fact]
    public async Task ConcurrentFinalizationAppliesEntitlementExactlyOnce()
    {
        // Two independent writers load the same Pending attempt and race to verify —
        // mirroring duplicate callbacks arriving in parallel. PostgreSQL xmin concurrency
        // lets exactly one commit; the loser must observe Verified (idempotent).
        var workspaceId = Guid.CreateVersion7();
        var planId = await SeedPlanAsync("race-plan", 2_000_000);
        var attempt = PaymentAttempt.Create(Guid.CreateVersion7(), workspaceId, planId, "zarinpal", 2_000_000, Now);
        attempt.AttachAuthority($"auth-race-{Guid.NewGuid():N}");
        await NewRepository().SaveChangesAsync(attempt);

        var writerA = NewScope();
        var writerB = NewScope();
        var loadedA = await writerA.Repository.FindByIdAsync(attempt.Id);
        var loadedB = await writerB.Repository.FindByIdAsync(attempt.Id);
        Assert.NotNull(loadedA);
        Assert.NotNull(loadedB);

        loadedA!.MarkVerified("ref-a", null, Now.AddMinutes(1));
        await writerA.Repository.SaveChangesAsync(loadedA);

        loadedB!.MarkVerified("ref-b", null, Now.AddMinutes(1));
        // The loser hits the xmin conflict; the repository translates it.
        await Assert.ThrowsAsync<PaymentConcurrencyException>(
            () => writerB.Repository.SaveChangesAsync(loadedB));

        // The winner's state is authoritative and exactly one reference exists.
        var final = await NewRepository().FindByIdAsync(attempt.Id);
        Assert.Equal("ref-a", final!.ProviderReferenceId);
    }

    [Fact]
    public async Task PlanPriceRoundTripsAndGatesPurchasability()
    {
        var paidId = await SeedPlanAsync("priced-plan", 3_400_000);
        var freePlan = Plan.Create(Guid.CreateVersion7(), "free-tier", "Free Tier");
        await NewScope().Plans.SaveChangesAsync(freePlan);

        var paid = await NewScope().Plans.FindByCodeAsync("PRICED-PLAN");
        Assert.Equal(paidId, paid!.Id);
        Assert.Equal(3_400_000, paid.AmountIrr);
        Assert.True(paid.IsPurchasable);

        var free = await NewScope().Plans.FindByCodeAsync("free-tier");
        Assert.Equal(0, free!.AmountIrr);
        Assert.False(free.IsPurchasable);
    }

    private async Task<Guid> SeedPlanAsync(string code, long amountIrr)
    {
        var plan = Plan.Create(Guid.CreateVersion7(), code, $"Plan {code}", amountIrr: amountIrr);
        await NewScope().Plans.SaveChangesAsync(plan);
        return plan.Id;
    }

    private (BillingDbContext Context, EfPlanRepository Plans, EfSubscriptionRepository Subscriptions, EfPaymentAttemptRepository Repository) NewScope()
    {
        var context = new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options);
        return (context, new EfPlanRepository(context), new EfSubscriptionRepository(context), new EfPaymentAttemptRepository(context));
    }

    private EfPaymentAttemptRepository NewRepository() => new(
        new BillingDbContext(new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql(fixture.ConnectionString)
            .Options));
}

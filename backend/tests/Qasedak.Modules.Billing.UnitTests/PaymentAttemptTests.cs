using Qasedak.Modules.Billing.Domain;
using Qasedak.Modules.Billing.Domain.Payments;
using Xunit;

namespace Qasedak.Modules.Billing.UnitTests;

/// <summary>Payment attempt state machine: transitions, guards, idempotency signals.</summary>
public sealed class PaymentAttemptTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateWithValidInputStartsPending()
    {
        var attempt = PaymentAttempt.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Zarinpal ", 1_500_000, Now);

        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal("zarinpal", attempt.ProviderId);
        Assert.Equal(1_500_000, attempt.AmountIrr);
        Assert.Null(attempt.Authority);
        Assert.False(attempt.IsTerminal);
    }

    [Fact]
    public void CreateRejectsNonpositiveAmountsAndEmptyProvider()
    {
        var ids = (Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        Assert.Throws<BillingDomainException>(() =>
            PaymentAttempt.Create(ids.Item1, ids.Item2, ids.Item3, "zarinpal", 0, Now));
        Assert.Throws<BillingDomainException>(() =>
            PaymentAttempt.Create(ids.Item1, ids.Item2, ids.Item3, "zarinpal", -5, Now));
        Assert.Throws<BillingDomainException>(() =>
            PaymentAttempt.Create(ids.Item1, ids.Item2, ids.Item3, "  ", 100, Now));
    }

    [Fact]
    public void AttachAuthorityStoresTrimmedAuthorityOnce()
    {
        var attempt = NewPending();
        attempt.AttachAuthority("  AuthorityValue  ");

        Assert.Equal("AuthorityValue", attempt.Authority);
    }

    [Fact]
    public void AttachAuthorityAfterTerminalStateIsRejected()
    {
        var verified = NewVerified();
        Assert.Throws<BillingDomainException>(() => verified.AttachAuthority("another"));
    }

    [Fact]
    public void MarkVerifiedTransitionsOnceThenRejectsDuplicates()
    {
        var attempt = NewPending();
        attempt.AttachAuthority("authority-1");
        attempt.MarkVerified("ref-123", "6037********1234", Now.AddMinutes(5));

        Assert.Equal(PaymentAttemptStatus.Verified, attempt.Status);
        Assert.Equal("ref-123", attempt.ProviderReferenceId);
        Assert.True(attempt.IsTerminal);
        // Duplicate verify (double callback / replay) surfaces as a domain signal.
        Assert.Throws<BillingDomainException>(() => attempt.MarkVerified("ref-again", null, Now.AddMinutes(6)));
    }

    [Fact]
    public void MarkVerifiedRequiresProviderReference()
    {
        var attempt = NewPending();
        attempt.AttachAuthority("authority-2");

        Assert.Throws<BillingDomainException>(() => attempt.MarkVerified("   ", null, Now));
    }

    [Fact]
    public void MarkFailedIsTerminalAndExcludesFurtherVerify()
    {
        var attempt = NewPending();
        attempt.MarkFailed(PaymentFailures.CanceledByUser, Now.AddMinutes(1));

        Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
        Assert.Equal(PaymentFailures.CanceledByUser, attempt.FailureCode);
        Assert.True(attempt.IsTerminal);
        Assert.Throws<BillingDomainException>(() => attempt.MarkVerified("ref-x", null, Now));
    }

    [Fact]
    public void MarkFailedRequiresFailureCode()
    {
        var attempt = NewPending();
        Assert.Throws<BillingDomainException>(() => attempt.MarkFailed("  ", Now));
    }

    [Fact]
    public void FromStateRoundtripsEveryField()
    {
        var original = NewPending();
        original.AttachAuthority("authority-9");
        original.MarkVerified("ref-9", "5022********9988", Now.AddMinutes(9));

        var restored = PaymentAttempt.FromState(
            original.Id,
            original.WorkspaceId,
            original.PlanId,
            original.ProviderId,
            original.AmountIrr,
            original.Status,
            original.Authority,
            null,
            original.ProviderReferenceId,
            original.FailureCode,
            original.MaskedCardPan,
            original.CreatedAtUtc,
            original.VerifiedAtUtc,
            original.FailedAtUtc);

        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Status, restored.Status);
        Assert.Equal(original.Authority, restored.Authority);
        Assert.Equal(original.ProviderReferenceId, restored.ProviderReferenceId);
        Assert.Equal(original.MaskedCardPan, restored.MaskedCardPan);
        Assert.Equal(original.VerifiedAtUtc, restored.VerifiedAtUtc);
    }

    private static PaymentAttempt NewPending() => PaymentAttempt.Create(
        Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "zarinpal", 2_400_000, Now);

    private static PaymentAttempt NewVerified()
    {
        var attempt = NewPending();
        attempt.AttachAuthority("authority-v");
        attempt.MarkVerified("ref-v", null, Now);
        return attempt;
    }
}

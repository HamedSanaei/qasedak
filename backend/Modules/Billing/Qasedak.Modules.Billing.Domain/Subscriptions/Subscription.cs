namespace Qasedak.Modules.Billing.Domain;

/// <summary>Subscription lifecycle states.</summary>
public enum SubscriptionStatus
{
    Trial = 1,
    Active = 2,
    PastDue = 3,
    Canceled = 4,
    Expired = 5,
}

/// <summary>
/// A workspace's subscription: at most one live subscription per workspace (enforced by
/// a partial unique index in persistence). Lifecycle transitions are explicit and
/// timestamped; the domain owns no clock — all times are parameters. Provider-agnostic:
/// no payment-provider identifiers exist here by design (provider selection is an open
/// decision tracked as BLOCKED M09-002).
/// </summary>
public sealed class Subscription
{
    public const int MaxPlanChangesTracked = 100;

    private readonly List<SubscriptionPeriod> _periods = [];

    private Subscription()
    {
    }

    public Guid Id { get; private init; }

    public Guid WorkspaceId { get; private init; }

    public Guid PlanId { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private init; }

    /// <summary>When the current entitlements stop being valid (null while no period is open).</summary>
    public DateTimeOffset? CurrentPeriodEndUtc => _periods.Count == 0 ? null : _periods[^1].EndsAtUtc;

    public DateTimeOffset? CanceledAtUtc { get; private set; }

    public IReadOnlyList<SubscriptionPeriod> Periods => _periods.AsReadOnly();

    public static Subscription StartTrial(Guid id, Guid workspaceId, Guid planId, DateTimeOffset startedAtUtc, DateTimeOffset trialEndsAtUtc)
    {
        Validate(id, workspaceId, planId);
        if (trialEndsAtUtc <= startedAtUtc)
        {
            throw new BillingDomainException("billing.invalidTrialWindow", "A trial must end after it starts.");
        }

        var subscription = new Subscription
        {
            Id = id,
            WorkspaceId = workspaceId,
            PlanId = planId,
            Status = SubscriptionStatus.Trial,
            StartedAtUtc = startedAtUtc,
        };
        subscription._periods.Add(new SubscriptionPeriod(startedAtUtc, trialEndsAtUtc));
        return subscription;
    }

    public static Subscription Activate(Guid id, Guid workspaceId, Guid planId, DateTimeOffset startedAtUtc, DateTimeOffset periodEndsAtUtc)
    {
        Validate(id, workspaceId, planId);
        if (periodEndsAtUtc <= startedAtUtc)
        {
            throw new BillingDomainException("billing.invalidPeriod", "A billing period must end after it starts.");
        }

        var subscription = new Subscription
        {
            Id = id,
            WorkspaceId = workspaceId,
            PlanId = planId,
            Status = SubscriptionStatus.Active,
            StartedAtUtc = startedAtUtc,
        };
        subscription._periods.Add(new SubscriptionPeriod(startedAtUtc, periodEndsAtUtc));
        return subscription;
    }

    /// <summary>Converts a live trial to a paid subscription opening a new billing period.</summary>
    public void ConvertTrialToActive(Guid planId, DateTimeOffset convertedAtUtc, DateTimeOffset periodEndsAtUtc)
    {
        EnsureStatus(SubscriptionStatus.Trial, "convert");
        ChangePlan(planId);
        if (periodEndsAtUtc <= convertedAtUtc)
        {
            throw new BillingDomainException("billing.invalidPeriod", "A billing period must end after it starts.");
        }

        _periods.Add(new SubscriptionPeriod(convertedAtUtc, periodEndsAtUtc));
        Status = SubscriptionStatus.Active;
    }

    /// <summary>Plan change on an active subscription; takes effect immediately.</summary>
    public void ChangePlan(Guid planId)
    {
        EnsureLive("change the plan of");
        if (planId == Guid.Empty)
        {
            throw new BillingDomainException("billing.invalidPlanId", "A subscription requires a valid plan.");
        }

        PlanId = planId;
    }

    /// <summary>Opens the next billing period on renewal (also clears PastDue).</summary>
    public void Renew(DateTimeOffset renewedAtUtc, DateTimeOffset nextPeriodEndsAtUtc)
    {
        EnsureLive("renew");
        if (nextPeriodEndsAtUtc <= renewedAtUtc)
        {
            throw new BillingDomainException("billing.invalidPeriod", "A billing period must end after it starts.");
        }

        if (_periods.Count >= MaxPlanChangesTracked)
        {
            throw new BillingDomainException("billing.tooManyPeriods", $"At most {MaxPlanChangesTracked} periods are tracked per subscription.");
        }

        _periods.Add(new SubscriptionPeriod(renewedAtUtc, nextPeriodEndsAtUtc));
        if (Status == SubscriptionStatus.PastDue)
        {
            Status = SubscriptionStatus.Active;
        }
    }

    /// <summary>Marks payment as failing while keeping entitlements grace-valid until expiry.</summary>
    public void MarkPastDue(DateTimeOffset atUtc)
    {
        _ = atUtc; // timestamp recorded by callers' audit trail; state change is immediate
        ApplyStatus(SubscriptionStatus.PastDue);
    }

    /// <summary>Cancels: terminal for this subscription row; entitlements run to period end.</summary>
    public void Cancel(DateTimeOffset canceledAtUtc)
    {
        if (Status is SubscriptionStatus.Canceled or SubscriptionStatus.Expired)
        {
            throw new BillingDomainException("billing.notCancelable", "The subscription is already terminated.");
        }

        Status = SubscriptionStatus.Canceled;
        CanceledAtUtc = canceledAtUtc;
    }

    /// <summary>Expires a lapsed subscription once its final period has passed.</summary>
    public void Expire(DateTimeOffset expiredAtUtc)
    {
        if (Status == SubscriptionStatus.Expired)
        {
            return; // idempotent
        }

        if (CurrentPeriodEndUtc is { } end && expiredAtUtc <= end && Status != SubscriptionStatus.Canceled)
        {
            throw new BillingDomainException("billing.notExpirable", "Cannot expire before the current period ends.");
        }

        Status = SubscriptionStatus.Expired;
    }

    /// <summary>Whether the workspace holds payable entitlements at the given instant.</summary>
    public bool IsEntitledAt(DateTimeOffset atUtc) =>
        Status is SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.PastDue
        && (CurrentPeriodEndUtc is not { } end || atUtc <= end);

    private static void Validate(Guid id, Guid workspaceId, Guid planId)
    {
        if (id == Guid.Empty)
        {
            throw new BillingDomainException("billing.invalidId", "A subscription requires an id.");
        }

        if (workspaceId == Guid.Empty)
        {
            throw new BillingDomainException("billing.workspaceRequired", "A subscription requires a workspace.");
        }

        if (planId == Guid.Empty)
        {
            throw new BillingDomainException("billing.planRequired", "A subscription requires a plan.");
        }
    }

    private void EnsureLive(string operation)
    {
        if (Status is SubscriptionStatus.Canceled or SubscriptionStatus.Expired)
        {
            throw new BillingDomainException("billing.notLive", $"Cannot {operation} a terminated subscription.");
        }
    }

    private void EnsureStatus(SubscriptionStatus expected, string operation)
    {
        if (Status != expected)
        {
            throw new BillingDomainException(
                "billing.wrongState",
                $"Cannot {operation} a subscription in state {Status}; expected {expected}.");
        }
    }

    private void ApplyStatus(SubscriptionStatus target)
    {
        EnsureLive("mark past due");
        Status = target;
    }

    /// <summary>Rehydration for persistence; state was valid when saved.</summary>
    public static Subscription FromState(
        Guid id,
        Guid workspaceId,
        Guid planId,
        SubscriptionStatus status,
        DateTimeOffset startedAtUtc,
        DateTimeOffset? canceledAtUtc,
        IReadOnlyList<SubscriptionPeriod> periods)
    {
        ArgumentNullException.ThrowIfNull(periods);
        var subscription = new Subscription
        {
            Id = id,
            WorkspaceId = workspaceId,
            PlanId = planId,
            Status = status,
            StartedAtUtc = startedAtUtc,
            CanceledAtUtc = canceledAtUtc,
        };
        subscription._periods.AddRange(periods);
        return subscription;
    }
}

/// <summary>An immutable opened billing period.</summary>
public sealed record SubscriptionPeriod(DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc);

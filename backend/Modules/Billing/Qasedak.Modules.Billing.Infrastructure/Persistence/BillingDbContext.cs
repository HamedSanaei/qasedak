using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Billing.Application;
using Qasedak.Modules.Billing.Application.Payments;
using Qasedak.Modules.Billing.Domain;
using Qasedak.Modules.Billing.Domain.Payments;

namespace Qasedak.Modules.Billing.Infrastructure.Persistence;

/// <summary>Module-owned persistence under the "billing" schema.</summary>
public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public const string Schema = "billing";

    public DbSet<PlanRow> Plans => Set<PlanRow>();

    public DbSet<SubscriptionRow> Subscriptions => Set<SubscriptionRow>();

    public DbSet<EntitlementRow> Entitlements => Set<EntitlementRow>();

    public DbSet<SubscriptionPeriodRow> SubscriptionPeriods => Set<SubscriptionPeriodRow>();

    public DbSet<PaymentAttemptRow> PaymentAttempts => Set<PaymentAttemptRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<PlanRow>(entity =>
        {
            entity.ToTable("plans");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).ValueGeneratedNever();
            entity.Property(p => p.Code).HasMaxLength(Plan.MaxCodeLength);
            entity.HasIndex(p => p.Code).IsUnique();
            entity.Property(p => p.Name).HasMaxLength(Plan.MaxNameLength);
            entity.Property(p => p.AmountIrr);

            entity.HasMany(p => p.Entitlements)
                .WithOne()
                .HasForeignKey(e => e.PlanId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(p => p.Entitlements).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<EntitlementRow>(entity =>
        {
            entity.ToTable("plan_entitlements");
            entity.HasKey(e => new { e.PlanId, e.FeatureKey });
            entity.Property(e => e.PlanId).ValueGeneratedNever();
            entity.Property(e => e.FeatureKey).HasMaxLength(64);
            entity.Property(e => e.Limit);
        });

        modelBuilder.Entity<SubscriptionRow>(entity =>
        {
            entity.ToTable("subscriptions");
            entity.HasKey(s => s.Id);
            entity.Property(s => s.Id).ValueGeneratedNever();
            // At most one subscription row per workspace, ever — terminated ones keep
            // history on the same row via status transitions.
            entity.HasIndex(s => s.WorkspaceId).IsUnique();
            entity.Property(s => s.Status).HasConversion<int>();
            entity.Property(s => s.CanceledAtUtc);

            entity.HasMany(s => s.Periods)
                .WithOne()
                .HasForeignKey(p => p.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(s => s.Periods).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<SubscriptionPeriodRow>(entity =>
        {
            entity.ToTable("subscription_periods");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).ValueGeneratedNever();
            entity.HasIndex(p => new { p.SubscriptionId, p.StartsAtUtc });
        });

        modelBuilder.Entity<PaymentAttemptRow>(entity =>
        {
            entity.ToTable("payment_attempts");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).ValueGeneratedNever();
            entity.Property(a => a.ProviderId).HasMaxLength(32);
            entity.Property(a => a.Status).HasConversion<int>();
            // Callback replay resolves to exactly one attempt; DB-level uniqueness is the
            // anti-replay mechanism (not in-memory locks).
            entity.HasIndex(a => a.Authority).IsUnique().HasFilter("\"Authority\" IS NOT NULL");
            entity.HasIndex(a => new { a.WorkspaceId, a.CreatedAtUtc });
            entity.Property(a => a.FailureCode).HasMaxLength(64);
            entity.Property(a => a.ProviderReferenceId).HasMaxLength(PaymentAttempt.MaxReferenceLength);
            entity.Property(a => a.MaskedCardPan).HasMaxLength(32);
            // Optimistic concurrency: concurrent callbacks race on this token and the
            // loser reloads to observe the winner's terminal state (idempotent).
            entity.Property(a => a.Version).IsRowVersion();
        });
    }
}

public sealed class PlanRow
{
    public Guid Id { get; init; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public long AmountIrr { get; set; }

    public List<EntitlementRow> Entitlements { get; init; } = [];
}

public sealed class EntitlementRow
{
    public Guid PlanId { get; init; }

    public string FeatureKey { get; init; } = string.Empty;

    public int Limit { get; set; }
}

public sealed class SubscriptionRow
{
    public Guid Id { get; init; }

    public Guid WorkspaceId { get; init; }

    public Guid PlanId { get; set; }

    public SubscriptionStatus Status { get; set; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? CanceledAtUtc { get; set; }

    public List<SubscriptionPeriodRow> Periods { get; init; } = [];
}

public sealed class SubscriptionPeriodRow
{
    public Guid Id { get; init; }

    public Guid SubscriptionId { get; init; }

    public DateTimeOffset StartsAtUtc { get; init; }

    public DateTimeOffset EndsAtUtc { get; init; }
}

/// <summary>Durable checkout attempt row. Card data is limited to provider-masked values.</summary>
public sealed class PaymentAttemptRow
{
    public Guid Id { get; init; }

    public Guid WorkspaceId { get; init; }

    public Guid PlanId { get; init; }

    public string ProviderId { get; set; } = string.Empty;

    public long AmountIrr { get; set; }

    public PaymentAttemptStatus Status { get; set; }

    public string? Authority { get; set; }

    public string? ProviderReferenceId { get; set; }

    public string? FailureCode { get; set; }

    public string? MaskedCardPan { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset? VerifiedAtUtc { get; set; }

    public DateTimeOffset? FailedAtUtc { get; set; }

    /// <summary>PostgreSQL xmin mapped by Npgsql as the concurrency token.</summary>
    public uint Version { get; private set; }
}

/// <summary>Plan catalog persistence.</summary>
public sealed class EfPlanRepository(BillingDbContext context) : IPlanRepository
{
    public Task<Plan?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        FindAndBuildAsync(id, cancellationToken);

    public async Task<Plan?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToLowerInvariant();
        var id = await context.Plans.AsNoTracking()
            .Where(p => p.Code == normalized)
            .Select(p => (Guid?)p.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return id is null ? null : await FindByIdAsync(id.Value, cancellationToken);
    }

    public async Task<IReadOnlyList<Plan>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await context.Plans.AsNoTracking()
            .Include(p => p.Entitlements)
            .OrderBy(p => p.Code)
            .ToListAsync(cancellationToken);
        return rows.Select(Build).ToList();
    }

    public async Task SaveChangesAsync(Plan plan, CancellationToken cancellationToken = default)
    {
        var row = context.Plans.Local.FirstOrDefault(r => r.Id == plan.Id)
            ?? await context.Plans.Include(p => p.Entitlements)
                .FirstOrDefaultAsync(r => r.Id == plan.Id, cancellationToken);

        if (row is null)
        {
            context.Plans.Add(new PlanRow
            {
                Id = plan.Id,
                Code = plan.Code,
                Name = plan.Name,
                AmountIrr = plan.AmountIrr,
                Entitlements = plan.Entitlements.Select(e => new EntitlementRow
                {
                    PlanId = plan.Id,
                    FeatureKey = e.FeatureKey,
                    Limit = e.Limit,
                }).ToList(),
            });
        }
        else
        {
            row.Name = plan.Name;
            row.AmountIrr = plan.AmountIrr;
            // Converge entitlement grants to the aggregate's set.
            foreach (var entitlement in plan.Entitlements)
            {
                var existing = row.Entitlements.FirstOrDefault(e => e.FeatureKey == entitlement.FeatureKey);
                if (existing is not null)
                {
                    existing.Limit = entitlement.Limit;
                }
                else
                {
                    row.Entitlements.Add(new EntitlementRow
                    {
                        PlanId = plan.Id,
                        FeatureKey = entitlement.FeatureKey,
                        Limit = entitlement.Limit,
                    });
                }
            }

            foreach (var stale in row.Entitlements
                .Where(e => plan.Entitlements.All(granted => granted.FeatureKey != e.FeatureKey))
                .ToList())
            {
                row.Entitlements.Remove(stale);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Plan?> FindAndBuildAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await context.Plans.AsNoTracking()
            .Include(p => p.Entitlements)
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        return row is null ? null : Build(row);
    }

    private static Plan Build(PlanRow row) => Plan.Create(
        row.Id,
        row.Code,
        row.Name,
        row.Entitlements.Select(e => Entitlement.Of(e.FeatureKey, e.Limit)),
        row.AmountIrr);
}

/// <summary>Subscription persistence with period history.</summary>
public sealed class EfSubscriptionRepository(BillingDbContext context) : ISubscriptionRepository
{
    public Task<Subscription?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        FindInternalAsync(id, cancellationToken);

    public async Task<Subscription?> FindByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var id = await context.Subscriptions.AsNoTracking()
            .Where(s => s.WorkspaceId == workspaceId)
            .Select(s => (Guid?)s.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return id is null ? null : await FindInternalAsync(id.Value, cancellationToken);
    }

    public async Task SaveChangesAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        var row = context.Subscriptions.Local.FirstOrDefault(r => r.Id == subscription.Id)
            ?? await context.Subscriptions.Include(s => s.Periods)
                .FirstOrDefaultAsync(r => r.Id == subscription.Id, cancellationToken);

        if (row is null)
        {
            context.Subscriptions.Add(ToRow(subscription));
        }
        else
        {
            // Mutable state only: plan pointer, status, cancel stamp. Periods are append-only.
            row.PlanId = subscription.PlanId;
            row.Status = subscription.Status;
            row.CanceledAtUtc = subscription.CanceledAtUtc;

            var knownPeriods = row.Periods.Select(p => (p.StartsAtUtc, p.EndsAtUtc)).ToHashSet();
            foreach (var period in subscription.Periods)
            {
                if (!knownPeriods.Contains((period.StartsAtUtc, period.EndsAtUtc)))
                {
                    row.Periods.Add(new SubscriptionPeriodRow
                    {
                        Id = Guid.CreateVersion7(),
                        SubscriptionId = subscription.Id,
                        StartsAtUtc = period.StartsAtUtc,
                        EndsAtUtc = period.EndsAtUtc,
                    });
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<Subscription?> FindInternalAsync(Guid id, CancellationToken cancellationToken)
    {
        var row = await context.Subscriptions.AsNoTracking()
            .Include(s => s.Periods)
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        return row is null ? null : Build(row);
    }

    private static Subscription Build(SubscriptionRow row) => Subscription.FromState(
        row.Id,
        row.WorkspaceId,
        row.PlanId,
        row.Status,
        row.StartedAtUtc,
        row.CanceledAtUtc,
        row.Periods.OrderBy(p => p.StartsAtUtc)
            .Select(p => new SubscriptionPeriod(p.StartsAtUtc, p.EndsAtUtc))
            .ToList());

    private static SubscriptionRow ToRow(Subscription subscription) => new()
    {
        Id = subscription.Id,
        WorkspaceId = subscription.WorkspaceId,
        PlanId = subscription.PlanId,
        Status = subscription.Status,
        StartedAtUtc = subscription.StartedAtUtc,
        CanceledAtUtc = subscription.CanceledAtUtc,
        Periods = subscription.Periods.Select(p => new SubscriptionPeriodRow
        {
            Id = Guid.CreateVersion7(),
            SubscriptionId = subscription.Id,
            StartsAtUtc = p.StartsAtUtc,
            EndsAtUtc = p.EndsAtUtc,
        }).ToList(),
    };
}

/// <summary>
/// Durable payment attempt persistence with concurrency-safe finalization. Loads are
/// TRACKED on purpose: the entity's original xmin is the concurrency token, so a stale
/// writer's SaveChanges collides exactly like two racing callbacks would in production.
/// </summary>
public sealed class EfPaymentAttemptRepository(BillingDbContext context) : IPaymentAttemptRepository
{
    public async Task<PaymentAttempt?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.PaymentAttempts
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        return row is null ? null : Build(row);
    }

    public async Task<PaymentAttempt?> FindByAuthorityAsync(string authority, CancellationToken cancellationToken = default)
    {
        var id = await context.PaymentAttempts
            .Where(a => a.Authority == authority)
            .Select(a => (Guid?)a.Id)
            .SingleOrDefaultAsync(cancellationToken);
        return id is null ? null : await FindByIdAsync(id.Value, cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentAttempt>> ListByWorkspaceAsync(Guid workspaceId, int limit = 50, CancellationToken cancellationToken = default)
    {
        var rows = await context.PaymentAttempts.AsNoTracking()
            .Where(a => a.WorkspaceId == workspaceId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return rows.Select(Build).ToList();
    }

    public async Task SaveChangesAsync(PaymentAttempt attempt, CancellationToken cancellationToken = default)
    {
        // Prefer the already-tracked row (original xmin preserved for conflict detection);
        // fall back to a fresh tracked load, then to insert for brand-new attempts.
        var row = context.PaymentAttempts.Local.FirstOrDefault(r => r.Id == attempt.Id)
            ?? await context.PaymentAttempts.FirstOrDefaultAsync(r => r.Id == attempt.Id, cancellationToken);

        if (row is null)
        {
            context.PaymentAttempts.Add(ToRow(attempt));
        }
        else
        {
            ApplyTo(row, attempt);
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            // Mirror any DB-assigned state back onto the aggregate (e.g. xmin bump).
        }
        catch (DbUpdateConcurrencyException exception)
        {
            // Translated so the Application layer stays persistence-agnostic.
            throw new PaymentConcurrencyException("The payment attempt was modified concurrently.", exception);
        }
    }

    private static void ApplyTo(PaymentAttemptRow row, PaymentAttempt attempt)
    {
        row.Status = attempt.Status;
        row.Authority = attempt.Authority;
        row.ProviderReferenceId = attempt.ProviderReferenceId;
        row.FailureCode = attempt.FailureCode;
        row.MaskedCardPan = attempt.MaskedCardPan;
        row.VerifiedAtUtc = attempt.VerifiedAtUtc;
        row.FailedAtUtc = attempt.FailedAtUtc;
    }

    private static PaymentAttempt Build(PaymentAttemptRow row) => PaymentAttempt.FromState(
        row.Id,
        row.WorkspaceId,
        row.PlanId,
        row.ProviderId,
        row.AmountIrr,
        row.Status,
        row.Authority,
        row.ProviderReferenceId,
        row.FailureCode,
        row.MaskedCardPan,
        row.CreatedAtUtc,
        row.VerifiedAtUtc,
        row.FailedAtUtc);

    private static PaymentAttemptRow ToRow(PaymentAttempt attempt) => new()
    {
        Id = attempt.Id,
        WorkspaceId = attempt.WorkspaceId,
        PlanId = attempt.PlanId,
        ProviderId = attempt.ProviderId,
        AmountIrr = attempt.AmountIrr,
        Status = attempt.Status,
        Authority = attempt.Authority,
        ProviderReferenceId = attempt.ProviderReferenceId,
        FailureCode = attempt.FailureCode,
        MaskedCardPan = attempt.MaskedCardPan,
        CreatedAtUtc = attempt.CreatedAtUtc,
        VerifiedAtUtc = attempt.VerifiedAtUtc,
        FailedAtUtc = attempt.FailedAtUtc,
    };
}

using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Billing.Application;
using Qasedak.Modules.Billing.Domain;

namespace Qasedak.Modules.Billing.Infrastructure.Persistence;

/// <summary>Module-owned persistence under the "billing" schema.</summary>
public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public const string Schema = "billing";

    public DbSet<PlanRow> Plans => Set<PlanRow>();

    public DbSet<SubscriptionRow> Subscriptions => Set<SubscriptionRow>();

    public DbSet<EntitlementRow> Entitlements => Set<EntitlementRow>();

    public DbSet<SubscriptionPeriodRow> SubscriptionPeriods => Set<SubscriptionPeriodRow>();

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
    }
}

public sealed class PlanRow
{
    public Guid Id { get; init; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

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
        row.Entitlements.Select(e => Entitlement.Of(e.FeatureKey, e.Limit)));
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

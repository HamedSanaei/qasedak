using Microsoft.EntityFrameworkCore;
using Qasedak.BuildingBlocks.Application.Scheduling;

namespace Qasedak.BuildingBlocks.Infrastructure.Scheduling;

/// <summary>Platform-owned persistence under the "platform" schema (durable scheduled work).</summary>
public sealed class ScheduledWorkDbContext(DbContextOptions<ScheduledWorkDbContext> options) : DbContext(options)
{
    public const string Schema = "platform";

    public DbSet<ScheduledWorkRow> Jobs => Set<ScheduledWorkRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ScheduledWorkRow>(entity =>
        {
            entity.ToTable("scheduled_jobs");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedNever();
            entity.Property(r => r.WorkType).HasMaxLength(80).IsRequired();
            entity.Property(r => r.IdempotencyKey).HasMaxLength(200).IsRequired();
            entity.Property(r => r.PayloadJson).IsRequired();
            entity.Property(r => r.Status).HasConversion<int>();
            entity.Property(r => r.LastFailureCode).HasMaxLength(100);
            entity.Property(r => r.LeaseOwner).HasMaxLength(80);
            // One logical job per idempotency key, enforced by the database.
            entity.HasIndex(r => r.IdempotencyKey).IsUnique();
            // Claim scan: due records first.
            entity.HasIndex(r => new { r.Status, r.NextAttemptAtUtc });
        });
    }
}

/// <summary>Persistence row for one durable scheduled-work record.</summary>
public sealed class ScheduledWorkRow
{
    public Guid Id { get; set; }

    public string WorkType { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public int PayloadVersion { get; set; }

    public Guid? ConnectedAccountId { get; set; }

    public Guid? WorkspaceId { get; set; }

    public DateTimeOffset DueAtUtc { get; set; }

    public ScheduledWorkStatus Status { get; set; }

    public int Attempts { get; set; }

    public int MaxAttempts { get; set; }

    public DateTimeOffset NextAttemptAtUtc { get; set; }

    public string? LastFailureCode { get; set; }

    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? StartedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }
}

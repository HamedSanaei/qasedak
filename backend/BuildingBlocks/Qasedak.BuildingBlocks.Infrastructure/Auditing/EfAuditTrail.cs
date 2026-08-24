using Microsoft.EntityFrameworkCore;
using Qasedak.BuildingBlocks.Application.Auditing;

namespace Qasedak.BuildingBlocks.Infrastructure.Auditing;

/// <summary>
/// Append-only audit persistence under the "audit" schema. The context exposes no update
/// or removal APIs and the EF model has no mutable properties — records are write-once.
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public const string Schema = "audit";

    public DbSet<AuditEntryRow> Entries => Set<AuditEntryRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AuditEntryRow>(entity =>
        {
            entity.ToTable("audit_entries");
            entity.HasKey(e => e.AuditId);
            entity.Property(e => e.AuditId).ValueGeneratedNever();
            entity.Property(e => e.Action).HasMaxLength(80);
            entity.Property(e => e.TargetType).HasMaxLength(80);
            entity.Property(e => e.TargetId).HasMaxLength(128);
            entity.HasIndex(e => new { e.WorkspaceId, e.AtUtc });
            entity.HasIndex(e => new { e.Action, e.AtUtc });
        });
    }
}

public sealed class AuditEntryRow
{
    public Guid AuditId { get; init; }

    public Guid? WorkspaceId { get; init; }

    public Guid? ActorUserId { get; init; }

    public string Action { get; init; } = string.Empty;

    public string? TargetType { get; init; }

    public string? TargetId { get; init; }

    public DateTimeOffset AtUtc { get; init; }

    public string? DetailsJson { get; init; }
}

/// <summary>Write-once audit sink over real PostgreSQL.</summary>
public sealed class EfAuditTrail(AuditDbContext context) : IAuditTrail
{
    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        context.Entries.Add(new AuditEntryRow
        {
            AuditId = entry.AuditId,
            WorkspaceId = entry.WorkspaceId,
            ActorUserId = entry.ActorUserId,
            Action = entry.Action,
            TargetType = entry.TargetType,
            TargetId = entry.TargetId,
            AtUtc = entry.AtUtc,
            DetailsJson = entry.DetailsJson,
        });
        await context.SaveChangesAsync(cancellationToken);
    }
}

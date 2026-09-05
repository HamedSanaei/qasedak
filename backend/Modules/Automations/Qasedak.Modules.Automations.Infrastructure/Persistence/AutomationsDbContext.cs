using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;

namespace Qasedak.Modules.Automations.Infrastructure.Persistence;

/// <summary>Module-owned persistence under the "automations" schema.</summary>
public sealed class AutomationsDbContext(DbContextOptions<AutomationsDbContext> options) : DbContext(options)
{
    public const string Schema = "automations";

    /// <summary>
    /// Nullable-struct converter: the opaque account binding persists as a nullable
    /// uuid. NULL marks legacy pre-M13-002 automations (preserved, never executed
    /// for exact-account events).
    /// </summary>
    internal static readonly ValueConverter<ChannelAccountId?, Guid?> ChannelAccountIdConverter = new(
        account => account.HasValue ? account.Value.Value : null,
        value => value.HasValue ? new ChannelAccountId(value.Value) : null);

    public DbSet<AutomationRow> Automations => Set<AutomationRow>();

    public DbSet<AutomationVersionRow> AutomationVersions => Set<AutomationVersionRow>();

    public DbSet<AutomationRunRow> AutomationRuns => Set<AutomationRunRow>();

    public DbSet<AutomationRunActionRow> AutomationRunActions => Set<AutomationRunActionRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AutomationRow>(entity =>
        {
            entity.ToTable("automations");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedNever();
            entity.Property(r => r.Name).HasMaxLength(Automation.MaxNameLength);
            entity.Property(r => r.ChannelAccountId).HasConversion(ChannelAccountIdConverter);
            entity.Property(r => r.Status).IsRequired();
            entity.HasIndex(r => new { r.WorkspaceId, r.CreatedAtUtc });
            entity.HasIndex(r => new { r.WorkspaceId, r.ChannelAccountId });
            entity.HasMany(r => r.Versions)
                .WithOne()
                .HasForeignKey(v => v.AutomationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AutomationVersionRow>(entity =>
        {
            entity.ToTable("automation_versions");
            entity.HasKey(v => new { v.AutomationId, v.Number });
            entity.Property(v => v.Number).ValueGeneratedNever();
            entity.Property(v => v.DefinitionJson).HasColumnName("definition_json").IsRequired();
            entity.HasIndex(v => v.AutomationId);
        });

        modelBuilder.Entity<AutomationRunRow>(entity =>
        {
            entity.ToTable("automation_runs");
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).ValueGeneratedNever();
            // The idempotency contract: one run per (automation, trigger event).
            entity.HasIndex(r => new { r.AutomationId, r.TriggerEventId }).IsUnique();
            entity.Property(r => r.TriggerEventId).HasMaxLength(200);
            entity.Property(r => r.Status).IsRequired();
        });

        modelBuilder.Entity<AutomationRunActionRow>(entity =>
        {
            entity.ToTable("automation_run_actions");
            entity.HasKey(a => new { a.RunId, a.ActionIndex });
            entity.Property(a => a.ActionIndex).ValueGeneratedNever();
            entity.Property(a => a.FailureCode).HasMaxLength(100);
        });
    }
}

/// <summary>Persistence row for the aggregate root.</summary>
public sealed class AutomationRow
{
    public Guid Id { get; init; }

    public Guid WorkspaceId { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>Exact bound account; null marks a legacy pre-M13-002 automation.</summary>
    public ChannelAccountId? ChannelAccountId { get; set; }

    public AutomationStatus Status { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset? ActivatedAtUtc { get; set; }

    public DateTimeOffset? DisabledAtUtc { get; set; }

    public bool CurrentVersionFrozen { get; set; }

    public List<AutomationVersionRow> Versions { get; init; } = [];
}

/// <summary>
/// Persistence row for one immutable definition snapshot. The JSON payload is the
/// module-owned storage format for <see cref="AutomationDefinition"/> — transport models
/// never appear here.
/// </summary>
public sealed class AutomationVersionRow
{
    public Guid AutomationId { get; init; }

    public int Number { get; init; }

    public string DefinitionJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>Module-owned JSON mapping for definitions (storage format, versioned by rows).</summary>
public static class AutomationDefinitionSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static string Serialize(AutomationDefinition definition) => JsonSerializer.Serialize(definition, Options);

    public static AutomationDefinition Deserialize(string json) =>
        JsonSerializer.Deserialize<AutomationDefinition>(json, Options)
        ?? throw new InvalidOperationException("Stored automation definition was empty.");
}

/// <summary>Persistence row for one execution record (idempotency ledger).</summary>
public sealed class AutomationRunRow
{
    public Guid Id { get; init; }

    public Guid AutomationId { get; init; }

    public int AutomationVersionNumber { get; init; }

    public string TriggerEventId { get; init; } = string.Empty;

    public Guid WorkspaceId { get; init; }

    public AutomationRunStatus Status { get; set; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public List<AutomationRunActionRow> Actions { get; init; } = [];
}

/// <summary>Persistence row for one action slot of a run.</summary>
public sealed class AutomationRunActionRow
{
    public Guid RunId { get; init; }

    public int ActionIndex { get; set; }

    public AutomationActionStatus Status { get; set; }

    public string? FailureCode { get; set; }
}

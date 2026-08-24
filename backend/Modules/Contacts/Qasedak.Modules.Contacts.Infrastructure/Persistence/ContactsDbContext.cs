using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Contacts.Application;
using Qasedak.Modules.Contacts.Domain;

namespace Qasedak.Modules.Contacts.Infrastructure.Persistence;

/// <summary>
/// Module-owned persistence under the "contacts" schema: contacts plus their social
/// identities. The workspace-wide unique index on identities is the persistence backstop
/// for the domain's identity-ownership invariant.
/// </summary>
public sealed class ContactsDbContext(DbContextOptions<ContactsDbContext> options) : DbContext(options)
{
    public const string Schema = "contacts";

    public DbSet<ContactRow> Contacts => Set<ContactRow>();

    public DbSet<ContactIdentityRow> ContactIdentities => Set<ContactIdentityRow>();

    public DbSet<ContactInteractionRow> ContactInteractions => Set<ContactInteractionRow>();

    public DbSet<ContactTagRow> ContactTags => Set<ContactTagRow>();

    public DbSet<ContactNoteRow> ContactNotes => Set<ContactNoteRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ContactRow>(entity =>
        {
            entity.ToTable("contacts");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Id).ValueGeneratedNever();
            entity.Property(c => c.DisplayName).HasMaxLength(Contact.MaxDisplayNameLength);
            entity.Property(c => c.Status).HasConversion<int>();
            entity.HasIndex(c => new { c.WorkspaceId, c.Status, c.LastSeenAtUtc });

            entity.HasMany(c => c.Identities)
                .WithOne()
                .HasForeignKey(i => i.ContactId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(c => c.Identities).UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.HasMany(c => c.Tags)
                .WithOne()
                .HasForeignKey(t => t.ContactId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(c => c.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.HasMany(c => c.Notes)
                .WithOne()
                .HasForeignKey(n => n.ContactId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(c => c.Notes).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<ContactIdentityRow>(entity =>
        {
            entity.ToTable("contact_identities");
            // One provider identity per channel per workspace — ever.
            entity.HasKey(i => new { i.ContactId, i.Channel, i.ProviderIdentity });
            entity.Property(i => i.ContactId).ValueGeneratedNever();
            entity.Property(i => i.Channel).HasMaxLength(Contact.MaxChannelLength);
            entity.Property(i => i.ProviderIdentity).HasMaxLength(Contact.MaxProviderIdentityLength);
            entity.HasIndex(i => new { i.WorkspaceId, i.Channel, i.ProviderIdentity }).IsUnique();
        });

        modelBuilder.Entity<ContactInteractionRow>(entity =>
        {
            entity.ToTable("contact_interactions");
            entity.HasKey(i => i.Id);
            entity.Property(i => i.Id).ValueGeneratedNever();
            // Inbox event ids are globally unique; the ledger is the replay boundary.
            entity.HasIndex(i => i.EventId).IsUnique();
            entity.Property(i => i.EventId).HasMaxLength(200);
            entity.Property(i => i.Kind).HasMaxLength(40);
            entity.HasIndex(i => new { i.WorkspaceId, i.OccurredAtUtc });
        });

        modelBuilder.Entity<ContactTagRow>(entity =>
        {
            entity.ToTable("contact_tags");
            entity.HasKey(t => new { t.ContactId, t.Tag });
            entity.Property(t => t.ContactId).ValueGeneratedNever();
            entity.Property(t => t.Tag).HasMaxLength(Contact.MaxTagLength);
            entity.HasIndex(t => new { t.WorkspaceId, t.Tag });
        });

        modelBuilder.Entity<ContactNoteRow>(entity =>
        {
            entity.ToTable("contact_notes");
            entity.HasKey(n => n.Id);
            entity.Property(n => n.Id).ValueGeneratedNever();
            entity.Property(n => n.Body).HasMaxLength(Contact.MaxNoteLength);
            entity.HasIndex(n => new { n.WorkspaceId, n.CreatedAtUtc });
        });
    }
}

/// <summary>Persistence row for a workspace-owned contact.</summary>
public sealed class ContactRow
{
    public Guid Id { get; init; }

    public Guid WorkspaceId { get; init; }

    public string DisplayName { get; set; } = string.Empty;

    public ContactStatus Status { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset FirstSeenAtUtc { get; set; }

    public DateTimeOffset LastSeenAtUtc { get; set; }

    public long InteractionCount { get; set; }

    public Guid? MergedIntoId { get; set; }

    public List<ContactIdentityRow> Identities { get; init; } = [];

    public List<ContactTagRow> Tags { get; init; } = [];

    public List<ContactNoteRow> Notes { get; init; } = [];
}

/// <summary>Persistence row for one bound social identity.</summary>
public sealed class ContactIdentityRow
{
    public Guid ContactId { get; init; }

    public Guid WorkspaceId { get; init; }

    public string Channel { get; init; } = string.Empty;

    public string ProviderIdentity { get; init; } = string.Empty;

    public DateTimeOffset LinkedAtUtc { get; init; }
}

/// <summary>Idempotency ledger row: one projected interaction event.</summary>
public sealed class ContactInteractionRow
{
    public Guid Id { get; init; }

    public Guid WorkspaceId { get; init; }

    public Guid ContactId { get; init; }

    public string EventId { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public DateTimeOffset OccurredAtUtc { get; init; }
}

/// <summary>One normalized tag on one contact.</summary>
public sealed class ContactTagRow
{
    public Guid ContactId { get; init; }

    public Guid WorkspaceId { get; init; }

    public string Tag { get; init; } = string.Empty;
}

/// <summary>An immutable note appended to a contact.</summary>
public sealed class ContactNoteRow
{
    public Guid Id { get; init; }

    public Guid ContactId { get; init; }

    public Guid WorkspaceId { get; init; }

    public string Body { get; init; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; init; }
}

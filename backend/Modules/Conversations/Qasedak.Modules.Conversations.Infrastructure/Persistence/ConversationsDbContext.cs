using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Conversations.Domain.Conversations;

namespace Qasedak.Modules.Conversations.Infrastructure.Persistence;

/// <summary>
/// Owns all Conversations-module tables under the module's logical PostgreSQL schema
/// ("conversations"). No other module may reference this context or its schema.
/// </summary>
public sealed class ConversationsDbContext(DbContextOptions<ConversationsDbContext> options) : DbContext(options)
{
    /// <summary>Module-owned logical schema per the data architecture contract.</summary>
    public const string Schema = "conversations";

    public DbSet<Conversation> Conversations => Set<Conversation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Conversation>(conversation =>
        {
            conversation.ToTable("conversations");
            conversation.HasKey(c => c.Id);
            conversation.Property(c => c.Id).ValueGeneratedNever();
            conversation.Property(c => c.Channel).HasMaxLength(32);
            conversation.Property(c => c.ParticipantId).HasMaxLength(64);
            conversation.Property(c => c.Status).HasConversion<int>();
            // One inbox thread per workspace + channel + counterpart.
            conversation.HasIndex(c => new { c.WorkspaceId, c.Channel, c.ParticipantId }).IsUnique();
            conversation.HasIndex(c => new { c.WorkspaceId, c.Status, c.LastMessageAtUtc });

            conversation.HasMany(c => c.Messages)
                .WithOne()
                .HasForeignKey(m => m.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            conversation.Navigation(c => c.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<Message>(message =>
        {
            message.ToTable("messages");
            message.HasKey(m => m.Id);
            message.Property(m => m.Id).ValueGeneratedNever();
            message.Property(m => m.ProviderMessageId).HasMaxLength(128);
            message.Property(m => m.SenderId).HasMaxLength(64);
            message.Property(m => m.Body).HasMaxLength(Conversation.MaxBodyLength);
            message.Property(m => m.Direction).HasConversion<int>();
            // Idempotent projection safety net: a provider identity is stored once.
            message.HasIndex(m => m.ProviderMessageId).IsUnique().HasFilter("\"ProviderMessageId\" IS NOT NULL");
        });
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Qasedak.BuildingBlocks.Domain;
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

    /// <summary>
    /// Nullable-struct converter: the opaque account identity persists as a nullable
    /// uuid. NULL marks legacy pre-M13-002 threads (readable, never outbound-routed).
    /// </summary>
    internal static readonly ValueConverter<ChannelAccountId?, Guid?> ChannelAccountIdConverter = new(
        account => account.HasValue ? account.Value.Value : null,
        value => value.HasValue ? new ChannelAccountId(value.Value) : null);

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
            conversation.Property(c => c.ChannelAccountId).HasConversion(ChannelAccountIdConverter);
            conversation.Property(c => c.ParticipantId).HasMaxLength(64);
            conversation.Property(c => c.Status).HasConversion<int>();
            // Exact natural key per workspace + channel + connected account + counterpart.
            // PostgreSQL treats NULLs as distinct, so any number of legacy (unresolved)
            // rows may share a triple while exact quadruples stay unique. The index name
            // is explicit because the conventional name exceeds PostgreSQL's 63-byte limit.
            conversation.HasIndex(c => new { c.WorkspaceId, c.Channel, c.ChannelAccountId, c.ParticipantId })
                .IsUnique()
                .HasDatabaseName("IX_conversations_exact_thread");
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

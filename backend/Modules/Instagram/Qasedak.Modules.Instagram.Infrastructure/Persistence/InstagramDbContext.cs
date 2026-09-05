using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence;

/// <summary>
/// Owns all Instagram-module tables under the module's logical PostgreSQL schema ("instagram").
/// No other module may reference this context or its schema.
/// </summary>
public sealed class InstagramDbContext(DbContextOptions<InstagramDbContext> options) : DbContext(options)
{
    /// <summary>Module-owned logical schema per the data architecture contract.</summary>
    public const string Schema = "instagram";

    public DbSet<ConnectedAccount> Accounts => Set<ConnectedAccount>();

    public DbSet<StoredAccountToken> AccountTokens => Set<StoredAccountToken>();

    public DbSet<WebhookInboxEntry> WebhookInbox => Set<WebhookInboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<ConnectedAccount>(account =>
        {
            account.ToTable("connected_accounts");
            account.HasKey(a => a.Id);
            account.Property(a => a.Id).ValueGeneratedNever();
            account.Property(a => a.ProviderUserId).HasMaxLength(64);
            account.Property(a => a.Path).HasConversion<int>();
            account.Property(a => a.Health).HasConversion<int>();
            account.Property(a => a.HealthDetail).HasMaxLength(256);
            // Scope snapshot stored as its comma-joined form; scopes never contain commas.
            // Materialization flows through the aggregate's List<string> backing field.
            account.Property(a => a.Scopes)
                .HasConversion(
                    scopes => string.Join(',', scopes),
                    value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList())
                .HasMaxLength(1024);
            // Only one active connection per workspace + provider identity; disconnected rows
            // remain as history and do not block reconnection.
            account.HasIndex(a => new { a.WorkspaceId, a.ProviderUserId })
                .IsUnique()
                .HasFilter("\"DisconnectedAtUtc\" IS NULL");
            // Routing-identity lookup backing ResolveActiveAccountAsync: every webhook
            // resolves active owners of one professional account id across workspaces.
            // Non-unique by design — duplicate active owners surface as Ambiguous and
            // fail closed instead of an order-dependent pick.
            account.HasIndex(a => a.ProviderUserId)
                .HasDatabaseName("IX_connected_accounts_active_routing_identity")
                .HasFilter("\"DisconnectedAtUtc\" IS NULL");
        });

        modelBuilder.Entity<StoredAccountToken>(token =>
        {
            token.ToTable("account_tokens");
            token.HasKey(t => t.AccountId);
            token.Property(t => t.AccountId).ValueGeneratedNever();
            token.Property(t => t.Ciphertext).HasMaxLength(4096);
        });

        modelBuilder.Entity<WebhookInboxEntry>(entry =>
        {
            entry.ToTable("webhook_inbox");
            // Event identity is the SHA-256 of the exact raw body; the primary key itself
            // makes duplicate deliveries a no-op.
            entry.HasKey(e => e.EventId);
            entry.Property(e => e.EventId).HasMaxLength(64);
            entry.Property(e => e.Topic).HasMaxLength(32);
            entry.HasIndex(e => new { e.Status, e.ReceivedAtUtc });
        });
    }
}

/// <summary>Persistence-only protected-token row: opaque ciphertext, never plaintext.</summary>
public sealed class StoredAccountToken
{
    public StoredAccountToken(Guid accountId, string ciphertext)
    {
        AccountId = accountId;
        Ciphertext = ciphertext;
    }

    /// <summary>References identity.connected_accounts.id within the same database.</summary>
    public Guid AccountId { get; private init; }

    /// <summary>Authenticated encryption of the raw access token.</summary>
    public string Ciphertext { get; private set; }

    /// <summary>Rotation replaces the ciphertext atomically.</summary>
    public void ReplaceCiphertext(string ciphertext) => Ciphertext = ciphertext;
}

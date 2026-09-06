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

    public DbSet<OAuthStateRow> OAuthStates => Set<OAuthStateRow>();

    /// <summary>Durable daily follower snapshots (M13-007); Instagram-owned analytics history.</summary>
    public DbSet<FollowerSnapshotRow> FollowerSnapshots => Set<FollowerSnapshotRow>();

    /// <summary>Global semantic effect claims (M13-009); one-shot provider effects (Private Reply / Public Reply).</summary>
    public DbSet<Effects.CommentEffectRow> CommentEffects => Set<Effects.CommentEffectRow>();

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
            // Domain-owned optimistic-concurrency counter (compare-and-swap):
            // rotation and terminal disconnect bump it, so a stale worker's
            // SaveChanges fails instead of overwriting newer state.
            // A plain concurrency token (not a rowversion): the aggregate owns the
            // value; legacy rows start at 0. Plain updates (health, profile,
            // subscriptions) keep the counter untouched.
            account.Property(a => a.Version).IsConcurrencyToken().HasDefaultValue(0u);
            account.Property(a => a.Username).HasMaxLength(128);
            account.Property(a => a.DisplayName).HasMaxLength(200);
            account.Property(a => a.ProfilePictureUrl).HasMaxLength(1024);
            account.Property(a => a.AccountType).HasMaxLength(32);
            account.Property(a => a.SubscriptionHealth)
                .HasConversion<int>()
                // Legacy rows predate subscription tracking: they default to Unknown
                // (never invent Healthy). New rows always carry the domain value.
                .HasDefaultValue(SubscriptionHealth.Unknown);
            account.Property(a => a.SubscriptionDetail).HasMaxLength(256);
        });

        modelBuilder.Entity<OAuthStateRow>(state =>
        {
            state.ToTable("oauth_states");
            state.HasKey(s => s.StateHash);
            state.Property(s => s.StateHash).HasMaxLength(64);
            state.Property(s => s.RedirectUri).HasMaxLength(1024);
            // One-time consume is enforced by conditional update, not by this index;
            // the unique hash makes concurrent issuance collisions fail loudly.
            state.HasIndex(s => s.StateHash).IsUnique();
            state.HasIndex(s => s.ExpiresAtUtc);
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

        modelBuilder.Entity<FollowerSnapshotRow>(snapshot =>
        {
            snapshot.ToTable("follower_snapshots");
            snapshot.HasKey(s => s.Id);
            snapshot.Property(s => s.Id).ValueGeneratedNever();
            snapshot.Property(s => s.FollowerCount);
            snapshot.Property(s => s.Provenance).HasConversion<int>();
            // PostgreSQL-enforced account/day uniqueness: concurrent daily jobs can never
            // create duplicate snapshots for the same account/day.
            snapshot.HasIndex(s => new { s.ConnectedAccountId, s.SnapshotDateUtc }).IsUnique();
            // History reads by account use the unique index prefix; no extra index needed.
        });

        modelBuilder.Entity<Effects.CommentEffectRow>(effect =>
        {
            effect.ToTable("comment_effects");
            effect.HasKey(e => e.Id);
            effect.Property(e => e.Id).ValueGeneratedNever();
            effect.Property(e => e.ProviderCommentId).HasMaxLength(128);
            effect.Property(e => e.OwnerOperationId).HasMaxLength(256);
            effect.Property(e => e.EffectType).HasConversion<int>();
            effect.Property(e => e.Status).HasConversion<int>();
            effect.Property(e => e.ProviderRecipientId).HasMaxLength(128);
            effect.Property(e => e.ProviderMessageId).HasMaxLength(256);
            effect.Property(e => e.FailureCode).HasMaxLength(128);
            // The global semantic claim: PostgreSQL-enforced uniqueness across automations,
            // redeliveries, restarts and application instances. Private and public effects
            // are distinct rows on the same comment.
            effect.HasIndex(e => new { e.ConnectedAccountId, e.ProviderCommentId, e.EffectType }).IsUnique();
            // Recovery/replay reads by semantic key use the unique index prefix; claim
            // transitions by Id use the primary key. No extra index needed.
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

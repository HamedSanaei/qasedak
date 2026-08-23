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
        });

        modelBuilder.Entity<StoredAccountToken>(token =>
        {
            token.ToTable("account_tokens");
            token.HasKey(t => t.AccountId);
            token.Property(t => t.AccountId).ValueGeneratedNever();
            token.Property(t => t.Ciphertext).HasMaxLength(4096);
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

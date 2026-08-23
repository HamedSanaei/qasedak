using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Identity.Domain.Users;
using Qasedak.Modules.Identity.Domain.Workspaces;

namespace Qasedak.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Owns all identity-module tables under the module's logical PostgreSQL schema ("identity").
/// No other module may reference this context or its schema.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    /// <summary>Module-owned logical schema per the data architecture contract.</summary>
    public const string Schema = "identity";

    public DbSet<User> Users => Set<User>();

    public DbSet<UserCredentials> UserCredentials => Set<UserCredentials>();

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.HasKey(u => u.Id);
            user.Property(u => u.Id).ValueGeneratedNever();
            user.Property(u => u.Email)
                .HasConversion(email => email.Value, value => EmailAddress.Create(value))
                .HasMaxLength(EmailAddress.MaxLength);
            user.Property(u => u.DisplayName).HasMaxLength(128);
            user.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<Workspace>(workspace =>
        {
            workspace.ToTable("workspaces");
            workspace.HasKey(w => w.Id);
            workspace.Property(w => w.Id).ValueGeneratedNever();
            workspace.Property(w => w.Name)
                .HasConversion(name => name.Value, value => WorkspaceName.Create(value))
                .HasMaxLength(WorkspaceName.MaxLength);

            workspace.HasMany(w => w.Memberships)
                .WithOne()
                .HasForeignKey(m => m.WorkspaceId)
                .OnDelete(DeleteBehavior.Cascade);

            // The aggregate owns its memberships; route materialization through the backing field.
            workspace.Navigation(w => w.Memberships)
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<UserCredentials>(credentials =>
        {
            credentials.ToTable("user_credentials");
            credentials.HasKey(c => c.UserId);
            credentials.Property(c => c.UserId).ValueGeneratedNever();
            credentials.Property(c => c.PasswordHash).HasMaxLength(512);
        });

        modelBuilder.Entity<Membership>(membership =>
        {
            membership.ToTable("memberships");
            membership.HasKey(m => m.Id);
            membership.Property(m => m.Id).ValueGeneratedNever();
            membership.Property(m => m.Role).HasConversion<int>();
            membership.HasIndex(m => new { m.WorkspaceId, m.UserId }).IsUnique();

            membership.HasOne<User>()
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

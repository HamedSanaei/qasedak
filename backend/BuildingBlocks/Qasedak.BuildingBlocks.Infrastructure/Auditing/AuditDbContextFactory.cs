using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Qasedak.BuildingBlocks.Infrastructure.Auditing;

/// <summary>Design-time factory for audit migrations (localhost fallback for tooling).</summary>
public sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("QASEDAK_AUDIT_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=qasedak;Username=qasedak;Password=qasedak";
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AuditDbContext.Schema))
            .Options;
        return new AuditDbContext(options);
    }
}

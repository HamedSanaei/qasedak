using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Qasedak.Modules.Billing.Infrastructure.Persistence;

namespace Qasedak.Modules.Billing.Infrastructure.Persistence;

/// <summary>Design-time factory for migration tooling; connection via env var or local fallback.</summary>
public sealed class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("QASEDAK_BILLING_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=qasedak;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", BillingDbContext.Schema))
            .Options;
        return new BillingDbContext(options);
    }
}

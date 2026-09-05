using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Qasedak.BuildingBlocks.Infrastructure.Scheduling;

/// <summary>Design-time factory for platform scheduled-work migrations.</summary>
public sealed class ScheduledWorkDbContextFactory : IDesignTimeDbContextFactory<ScheduledWorkDbContext>
{
    public ScheduledWorkDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("QASEDAK_PLATFORM_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=qasedak;Username=qasedak;Password=qasedak";
        var options = new DbContextOptionsBuilder<ScheduledWorkDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ScheduledWorkDbContext.Schema))
            .Options;
        return new ScheduledWorkDbContext(options);
    }
}

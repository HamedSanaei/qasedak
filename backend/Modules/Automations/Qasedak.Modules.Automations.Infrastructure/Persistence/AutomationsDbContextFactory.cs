using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Qasedak.Modules.Automations.Infrastructure.Persistence;

namespace Qasedak.Modules.Automations.Infrastructure;

/// <summary>Design-time factory for `dotnet ef` tooling (no host required).</summary>
public sealed class AutomationsDbContextFactory : IDesignTimeDbContextFactory<AutomationsDbContext>
{
    public AutomationsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("QASEDAK_AUTOMATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=qasedak_automations_design;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<AutomationsDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AutomationsDbContext.Schema))
            .Options;
        return new AutomationsDbContext(options);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Qasedak.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so `dotnet ef` can generate/inspect migrations without booting the host.
/// Connection string resolution order: --connection arg, environment variable, then
/// appsettings.Development.json / appsettings.json of the API composition root when present.
/// </summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            args.FirstOrDefault(a => a.StartsWith("--connection=", StringComparison.Ordinal)) is { } arg
                ? arg["--connection=".Length..]
                : Environment.GetEnvironmentVariable("QASEDAK_IDENTITY_CONNECTION")
                ?? new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: true)
                    .AddJsonFile("appsettings.Development.json", optional: true)
                    .Build()
                    .GetConnectionString("Identity")
                ?? "Host=localhost;Port=5432;Database=qasedak_design_time;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new IdentityDbContext(options);
    }
}

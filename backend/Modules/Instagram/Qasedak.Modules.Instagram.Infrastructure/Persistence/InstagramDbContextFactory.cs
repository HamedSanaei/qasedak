using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Qasedak.Modules.Instagram.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so `dotnet ef` can generate/inspect migrations without booting the host.
/// </summary>
public sealed class InstagramDbContextFactory : IDesignTimeDbContextFactory<InstagramDbContext>
{
    public InstagramDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("QASEDAK_INSTAGRAM_CONNECTION")
            ?? new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .Build()
                .GetConnectionString("Instagram")
            ?? "Host=localhost;Port=5432;Database=qasedak_design_time;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<InstagramDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new InstagramDbContext(options);
    }
}

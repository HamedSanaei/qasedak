using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.BuildingBlocks.Application;
using Qasedak.BuildingBlocks.Application.Auditing;
using Qasedak.BuildingBlocks.Infrastructure.Auditing;
using Qasedak.BuildingBlocks.Infrastructure.Diagnostics;

namespace Qasedak.BuildingBlocks.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddQasedakBuildingBlocks(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        // Correlation plumbing: scoped accessor filled by CorrelationMiddleware.
        services.AddScoped<ICorrelationContextAccessor, CorrelationContextAccessor>();

        // Append-only audit trail under the "audit" schema. Registered only when a
        // connection string is configured (composition root decides); module code depends
        // solely on the BuildingBlocks.Application port.
        return services;
    }

    /// <summary>Binds the append-only audit trail to a real database.</summary>
    public static IServiceCollection AddQasedakAuditTrail(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AuditDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AuditDbContext.Schema)));
        services.AddScoped<IAuditTrail, EfAuditTrail>();
        return services;
    }

    public static IApplicationBuilder UseQasedakCorrelation(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationMiddleware>();
}



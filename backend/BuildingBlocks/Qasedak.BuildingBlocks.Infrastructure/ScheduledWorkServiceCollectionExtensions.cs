using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.BuildingBlocks.Application.Scheduling;
using Qasedak.BuildingBlocks.Infrastructure.Scheduling;

namespace Qasedak.BuildingBlocks.Infrastructure;

/// <summary>Composition helper for durable scheduled work (M13-004).</summary>
public static class ScheduledWorkServiceCollectionExtensions
{
    /// <summary>
    /// Binds durable scheduled work to a real database. Module handlers register via
    /// <see cref="AddScheduledWorkHandler{THandler}"/>; the dispatcher polls only when
    /// at least the store exists. Payloads never contain secrets (guarded at enqueue).
    /// </summary>
    public static IServiceCollection AddQasedakScheduledWork(
        this IServiceCollection services, string connectionString, IConfiguration configuration)
    {
        services.Configure<ScheduledWorkOptions>(configuration.GetSection(ScheduledWorkOptions.SectionName));
        services.AddDbContext<ScheduledWorkDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ScheduledWorkDbContext.Schema)));
        services.AddScoped<IScheduledWorkStore, EfScheduledWorkStore>();
        services.AddSingleton<ScheduledWorkMetrics>();
        services.AddHostedService<ScheduledWorkDispatcher>();
        return services;
    }

    /// <summary>Registers one module-owned work handler (resolved per dispatch scope).</summary>
    public static IServiceCollection AddScheduledWorkHandler<THandler>(this IServiceCollection services)
        where THandler : class, IScheduledWorkHandler
    {
        services.AddScoped<IScheduledWorkHandler, THandler>();
        return services;
    }
}

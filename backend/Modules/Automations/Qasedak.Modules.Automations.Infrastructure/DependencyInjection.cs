using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Infrastructure.Persistence;

namespace Qasedak.Modules.Automations.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAutomationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Module-owned persistence under the "automations" schema.
        services.AddDbContext<AutomationsDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Automations"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", AutomationsDbContext.Schema)));
        services.AddScoped<IAutomationRepository, EfAutomationRepository>();
        services.AddScoped<IAutomationRunRepository, EfAutomationRunRepository>();
        // ExecuteAutomationUseCase is registered with its IAutomationActionDispatcher
        // binding when the comment→DM flow lands (M06-005); registering it earlier would
        // fail host validation with an unresolvable port.

        return services;
    }
}

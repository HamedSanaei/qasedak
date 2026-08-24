using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Modules.Billing.Application;
using Qasedak.Modules.Billing.Infrastructure.Persistence;

namespace Qasedak.Modules.Billing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBillingModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Module-owned persistence under the "billing" schema. Provider-neutral by design:
        // no payment-provider adapter is registered until the provider selection ADR lands
        // (tracked as BLOCKED M09-002).
        services.AddDbContext<BillingDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Billing"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", BillingDbContext.Schema)));
        services.AddScoped<IPlanRepository, EfPlanRepository>();
        services.AddScoped<ISubscriptionRepository, EfSubscriptionRepository>();
        services.AddScoped<StartSubscriptionUseCase>();
        services.AddScoped<ResolveWorkspaceEntitlementsUseCase>();

        return services;
    }
}

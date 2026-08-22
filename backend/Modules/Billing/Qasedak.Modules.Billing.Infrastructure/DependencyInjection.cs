using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Qasedak.Modules.Billing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBillingModule(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        return services;
    }
}

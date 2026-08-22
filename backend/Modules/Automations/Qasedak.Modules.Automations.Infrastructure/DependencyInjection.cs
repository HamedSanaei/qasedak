using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Qasedak.Modules.Automations.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAutomationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        return services;
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Qasedak.Modules.Instagram.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInstagramModule(this IServiceCollection services, IConfiguration configuration)
    {
        _ = configuration;
        return services;
    }
}

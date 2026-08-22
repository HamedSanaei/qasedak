using Microsoft.Extensions.DependencyInjection;
using Qasedak.BuildingBlocks.Application;

namespace Qasedak.BuildingBlocks.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddQasedakBuildingBlocks(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        return services;
    }
}

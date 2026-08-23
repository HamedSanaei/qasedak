using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.Infrastructure.Webhooks;

namespace Qasedak.Modules.Instagram.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInstagramModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MetaWebhookOptions>(configuration.GetSection(MetaWebhookOptions.SectionName));
        services.AddSingleton<IWebhookSignatureVerifier, HmacWebhookSignatureVerifier>();
        services.AddSingleton<IWebhookSubscriptionValidator, MetaWebhookSubscriptionValidator>();
        return services;
    }
}

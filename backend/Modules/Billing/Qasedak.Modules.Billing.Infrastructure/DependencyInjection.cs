using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Billing.Application;
using Qasedak.Modules.Billing.Application.Payments;
using Qasedak.Modules.Billing.Infrastructure.Endpoints;
using Qasedak.Modules.Billing.Infrastructure.Payments;
using Qasedak.Modules.Billing.Infrastructure.Persistence;

namespace Qasedak.Modules.Billing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBillingModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Module-owned persistence under the "billing" schema.
        services.AddDbContext<BillingDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Billing"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", BillingDbContext.Schema)));
        services.AddScoped<IPlanRepository, EfPlanRepository>();
        services.AddScoped<ISubscriptionRepository, EfSubscriptionRepository>();
        services.AddScoped<IPaymentAttemptRepository, EfPaymentAttemptRepository>();
        services.AddScoped<StartSubscriptionUseCase>();
        services.AddScoped<ResolveWorkspaceEntitlementsUseCase>();

        // Provider-neutral payment gateways (ADR-008). Adapters own all protocol detail;
        // each is inert unless its options are Enabled via environment configuration.
        // Typed HttpClient registration: the adapter resolves with its configured client.
        services.AddHttpClient<ZarinpalPaymentGateway>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<ZarinpalOptions>>().Value;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.Timeout = TimeSpan.FromSeconds(20);
        });
        services.Configure<ZarinpalOptions>(configuration.GetSection(ZarinpalOptions.SectionName));
        services.Configure<BehpardakhtOptions>(configuration.GetSection(BehpardakhtOptions.SectionName));
        services.AddScoped<BehpardakhtMellatPaymentGateway>();
        services.AddScoped<IPaymentGatewayResolver, PaymentGatewayResolver>();

        services.AddScoped<CreateCheckoutUseCase>();
        services.AddScoped<FinalizePaymentUseCase>();
        services.AddScoped<PaymentQueries>();

        return services;
    }
}

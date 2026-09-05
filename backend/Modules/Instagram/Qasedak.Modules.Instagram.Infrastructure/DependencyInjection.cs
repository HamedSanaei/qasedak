using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.Messaging;
using Qasedak.Modules.Instagram.Infrastructure.OAuth;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Infrastructure.Protection;
using Qasedak.Modules.Instagram.Infrastructure.Webhooks;

namespace Qasedak.Modules.Instagram.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInstagramModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MetaWebhookOptions>(configuration.GetSection(MetaWebhookOptions.SectionName));
        services.Configure<MetaOAuthOptions>(configuration.GetSection(MetaOAuthOptions.SectionName));
        services.Configure<MetaGraphOptions>(configuration.GetSection(MetaGraphOptions.SectionName));
        services.Configure<TokenProtectionOptions>(configuration.GetSection(TokenProtectionOptions.SectionName));
        services.AddSingleton<IWebhookSignatureVerifier, HmacWebhookSignatureVerifier>();
        services.AddSingleton<IWebhookSubscriptionValidator, MetaWebhookSubscriptionValidator>();
        services.AddSingleton<IAuthorizationUrlBuilder, InstagramAuthorizationUrlBuilder>();

        // Typed OAuth HTTP client; the app secret never leaves server-side code.
        services.AddHttpClient(GraphInstagramOAuthClient.HttpClientName);
        services.AddSingleton(sp => new GraphInstagramOAuthClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GraphInstagramOAuthClient.HttpClientName),
            sp.GetRequiredService<IOptions<MetaOAuthOptions>>(),
            sp.GetRequiredService<IOptions<MetaGraphOptions>>()));
        services.AddSingleton<IMetaOAuthClient>(sp => sp.GetRequiredService<GraphInstagramOAuthClient>());

        // Live token inspection for health evaluation (OQ-3 taxonomy lives here).
        services.AddHttpClient(GraphInstagramTokenInspector.HttpClientName);
        services.AddSingleton(sp => new GraphInstagramTokenInspector(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GraphInstagramTokenInspector.HttpClientName),
            sp.GetRequiredService<IOptions<MetaGraphOptions>>()));
        services.AddSingleton<IMetaTokenInspector>(sp => sp.GetRequiredService<GraphInstagramTokenInspector>());

        // Messaging send API (M05-004): typed client + structured failure taxonomy.
        services.Configure<MetaMessagingOptions>(configuration.GetSection(MetaMessagingOptions.SectionName));
        services.AddHttpClient(GraphInstagramMessagingClient.HttpClientName);
        services.AddSingleton(sp => new GraphInstagramMessagingClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GraphInstagramMessagingClient.HttpClientName),
            sp.GetRequiredService<IOptions<MetaMessagingOptions>>(),
            sp.GetRequiredService<IOptions<MetaGraphOptions>>()));
        services.AddSingleton<IInstagramMessagingClient>(sp => sp.GetRequiredService<GraphInstagramMessagingClient>());

        // Module-owned persistence under the "instagram" schema.
        services.AddDbContext<InstagramDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Instagram"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", InstagramDbContext.Schema)));
        services.AddScoped<IConnectedAccountRepository, EfConnectedAccountRepository>();
        services.AddSingleton<ITokenProtector, AesGcmTokenProtector>();
        services.AddScoped<IProtectedTokenStore, ProtectedTokenStore>();
        // Durable idempotent inbox: replaces the M04-001 placeholder boundary.
        services.AddScoped<IMetaWebhookIngester, InboxWebhookIngester>();
        // Post-ingest seam: no-op by default; composition roots bridge downstream consumers.
        services.AddSingleton<IWebhookPostIngestProcessor, NullWebhookPostIngestProcessor>();
        services.AddScoped<IWebhookInboxStore, EfWebhookInboxStore>();
        services.AddSingleton<IIntegrationEventDispatcher, LoggingIntegrationEventDispatcher>();
        services.AddScoped<ProcessPendingWebhookEventsUseCase>();

        // Webhook observability: shared meter; backlog gauge attached once the host starts.
        var webhookMetrics = new WebhookMetrics();
        services.AddSingleton(webhookMetrics);
        services.AddHostedService<WebhookBacklogGauge>();

        // Account lifecycle use cases.
        services.AddScoped<ConnectInstagramAccountUseCase>();
        services.AddScoped<DisconnectInstagramAccountUseCase>();
        services.AddScoped<ListWorkspaceConnectionsUseCase>();
        services.AddScoped<EvaluateAccountHealthUseCase>();

        return services;
    }
}

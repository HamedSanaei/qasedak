using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Media;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Application.Subscriptions;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.Media;
using Qasedak.Modules.Instagram.Infrastructure.Messaging;
using Qasedak.Modules.Instagram.Infrastructure.OAuth;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Infrastructure.Profiles;
using Qasedak.Modules.Instagram.Infrastructure.Protection;
using Qasedak.Modules.Instagram.Infrastructure.Subscriptions;
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
        services.AddScoped<RepairSubscriptionUseCase>();
        services.AddScoped<RefreshInstagramTokenUseCase>();
        services.AddScoped<IOAuthStateStore, EfOAuthStateStore>();

        // Professional profile + webhook subscription adapters (M13-005): focused
        // clients over the shared Graph transport; no raw DTOs leave Infrastructure.
        services.AddHttpClient(GraphAccountProfileClient.HttpClientName);
        services.AddSingleton(sp => new GraphAccountProfileClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GraphAccountProfileClient.HttpClientName),
            sp.GetRequiredService<IOptions<MetaGraphOptions>>()));
        services.AddSingleton<IAccountProfileClient>(sp => sp.GetRequiredService<GraphAccountProfileClient>());
        services.AddHttpClient(GraphSubscriptionClient.HttpClientName);
        services.AddSingleton(sp => new GraphSubscriptionClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GraphSubscriptionClient.HttpClientName),
            sp.GetRequiredService<IOptions<MetaGraphOptions>>()));
        services.AddSingleton<ISubscriptionClient>(sp => sp.GetRequiredService<GraphSubscriptionClient>());

        // Media catalog (M13-006): focused adapter over the shared Graph transport
        // plus the opaque account-bound cursor codec; no provider DTOs leave here.
        services.AddSingleton<IMediaCursorCodec, MediaCatalogCursorCodec>();
        services.AddHttpClient(GraphMediaCatalogClient.HttpClientName);
        services.AddSingleton(sp => new GraphMediaCatalogClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GraphMediaCatalogClient.HttpClientName),
            sp.GetRequiredService<IOptions<MetaGraphOptions>>(),
            sp.GetRequiredService<IMediaCursorCodec>()));
        services.AddSingleton<IMediaCatalogClient>(sp => sp.GetRequiredService<GraphMediaCatalogClient>());
        services.AddScoped<ListMediaPageUseCase>();

        return services;
    }
}

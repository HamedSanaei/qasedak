using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.Effects;
using Qasedak.Modules.Instagram.Application.FollowerSnapshots;
using Qasedak.Modules.Instagram.Application.Insights;
using Qasedak.Modules.Instagram.Application.Media;
using Qasedak.Modules.Instagram.Application.Messaging;
using Qasedak.Modules.Instagram.Application.OAuth;
using Qasedak.Modules.Instagram.Application.Subscriptions;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.Infrastructure.Effects;
using Qasedak.Modules.Instagram.Infrastructure.Graph;
using Qasedak.Modules.Instagram.Infrastructure.Insights;
using Qasedak.Modules.Instagram.Infrastructure.Media;
using Qasedak.Modules.Instagram.Infrastructure.Messaging;
using Qasedak.Modules.Instagram.Infrastructure.OAuth;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Infrastructure.Profiles;
using Qasedak.Modules.Instagram.Infrastructure.Protection;
using Qasedak.Modules.Instagram.Infrastructure.Snapshots;
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
        var messageSendMetrics = new MessageSendMetrics();
        services.AddSingleton(messageSendMetrics);
        services.AddSingleton<IMessageSendObservability>(sp => sp.GetRequiredService<MessageSendMetrics>());
        services.AddSingleton(sp => new GraphInstagramMessagingClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GraphInstagramMessagingClient.HttpClientName),
            sp.GetRequiredService<IOptions<MetaMessagingOptions>>(),
            sp.GetRequiredService<IOptions<MetaGraphOptions>>(),
            sp.GetRequiredService<MessageSendMetrics>()));
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
        // Exact-account inbound enrichment (M13-008): the deterministic M13-002 resolver
        // behind a focused port; normalization stays pure, resolution happens once at the
        // processing boundary before dispatch.
        services.AddScoped<IInboundAccountResolver, ConnectedAccountInboundResolver>();

        // Webhook observability: shared meter (also implements the Application
        // normalization-observability port); backlog gauge attached once the host starts.
        var webhookMetrics = new WebhookMetrics();
        services.AddSingleton(webhookMetrics);
        services.AddSingleton<IWebhookNormalizationObservability>(sp => sp.GetRequiredService<WebhookMetrics>());
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

        // Insights (M13-007): focused adapter over the shared Graph transport, the
        // verified metric registry, bounded-concurrency policy and observability.
        // The options object is registered directly (the Application layer has no
        // Microsoft.Extensions.Options dependency).
        var insightsOptions = configuration.GetSection(InsightsOptions.SectionName).Get<InsightsOptions>() ?? new InsightsOptions();
        services.AddSingleton(insightsOptions);
        var insightsMetrics = new InsightsMetrics();
        services.AddSingleton(insightsMetrics);
        services.AddSingleton<IInsightsObservability>(sp => sp.GetRequiredService<InsightsMetrics>());
        services.AddHttpClient(GraphInstagramInsightsClient.HttpClientName);
        services.AddSingleton(sp => new GraphInstagramInsightsClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GraphInstagramInsightsClient.HttpClientName),
            sp.GetRequiredService<IOptions<MetaGraphOptions>>()));
        services.AddSingleton<IInstagramInsightsClient>(sp => sp.GetRequiredService<GraphInstagramInsightsClient>());

        // Follower snapshots (M13-007): Instagram-owned durable daily history with
        // PostgreSQL account/day uniqueness + provenance precedence.
        services.AddScoped<IFollowerSnapshotStore, EfFollowerSnapshotStore>();
        services.AddScoped<FollowerSnapshotUseCase>();
        services.AddScoped<GetInstagramOverviewUseCase>();
        services.AddScoped<GetFollowerHistoryUseCase>();
        // Production-hardening: existing active accounts acquire today's snapshot job
        // at each host start (idempotent, DB-only, bounded).
        services.AddHostedService<FollowerSnapshotScheduleBootstrap>();

        // Comment effects (M13-009): global semantic claim ledger + focused provider
        // adapters over the shared Graph transport, plus low-cardinality observability.
        services.AddScoped<ICommentEffectLedger, EfCommentEffectLedger>();
        services.AddSingleton<CommentEffectMetrics>();
        services.AddSingleton<ICommentEffectObservability>(sp => sp.GetRequiredService<CommentEffectMetrics>());
        services.AddHttpClient(GraphCommentPrivateReplyClient.HttpClientName);
        services.AddSingleton(sp => new GraphCommentPrivateReplyClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GraphCommentPrivateReplyClient.HttpClientName),
            sp.GetRequiredService<IOptions<MetaMessagingOptions>>(),
            sp.GetRequiredService<IOptions<MetaGraphOptions>>()));
        services.AddSingleton<ICommentPrivateReplyClient>(sp => sp.GetRequiredService<GraphCommentPrivateReplyClient>());
        services.AddHttpClient(GraphCommentPublicReplyClient.HttpClientName);
        services.AddSingleton(sp => new GraphCommentPublicReplyClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GraphCommentPublicReplyClient.HttpClientName),
            sp.GetRequiredService<IOptions<MetaGraphOptions>>()));
        services.AddSingleton<ICommentPublicReplyClient>(sp => sp.GetRequiredService<GraphCommentPublicReplyClient>());
        services.AddHttpClient(GraphCommentReferenceReader.HttpClientName);
        services.AddSingleton(sp => new GraphCommentReferenceReader(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(GraphCommentReferenceReader.HttpClientName),
            sp.GetRequiredService<IOptions<MetaGraphOptions>>()));
        services.AddSingleton<ICommentReferenceReader>(sp => sp.GetRequiredService<GraphCommentReferenceReader>());
        services.AddScoped<CommentPrivateReplyCoordinator>();

        return services;
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Modules.Conversations.Application.Conversations;
using Qasedak.Modules.Conversations.Infrastructure.Persistence;

namespace Qasedak.Modules.Conversations.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddConversationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Module-owned persistence under the "conversations" schema.
        services.AddDbContext<ConversationsDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Conversations"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ConversationsDbContext.Schema)));
        services.AddScoped<IConversationRepository, EfConversationRepository>();
        services.AddScoped<IConversationQueries, EfConversationQueries>();
        services.AddScoped<ProjectInboundMessageUseCase>();
        services.AddScoped<SendReplyUseCase>();

        return services;
    }
}

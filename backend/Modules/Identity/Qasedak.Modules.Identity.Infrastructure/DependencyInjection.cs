using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Modules.Identity.Application.Authentication;
using Qasedak.Modules.Identity.Application.Workspaces;
using Qasedak.Modules.Identity.Infrastructure.Authentication;
using Qasedak.Modules.Identity.Infrastructure.Endpoints;
using Qasedak.Modules.Identity.Infrastructure.Persistence;

namespace Qasedak.Modules.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<IdentityAuthOptions>(configuration.GetSection(IdentityAuthOptions.SectionName));
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ISecurityTokenIssuer, HmacSecurityTokenIssuer>();

        services.AddDbContext<IdentityDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Identity"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema)));
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IWorkspaceRepository, EfWorkspaceRepository>();

        services.AddScoped<RegisterUserUseCase>();
        services.AddScoped<AuthenticateUserUseCase>();
        services.AddScoped<CreateWorkspaceUseCase>();
        services.AddScoped<ListWorkspaceMembersUseCase>();

        services.AddAuthentication(SecurityTokenAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SecurityTokenAuthenticationHandler>(
                SecurityTokenAuthenticationHandler.SchemeName, _ => { });
        services.AddAuthorization();

        return services;
    }
}

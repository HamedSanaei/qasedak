using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qasedak.Modules.Contacts.Application;
using Qasedak.Modules.Contacts.Infrastructure.Persistence;

namespace Qasedak.Modules.Contacts.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddContactsModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Module-owned persistence under the "contacts" schema.
        services.AddDbContext<ContactsDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("Contacts"),
                npgsql => npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ContactsDbContext.Schema)));
        services.AddScoped<IContactRepository, EfContactRepository>();
        services.AddScoped<IContactQueries, EfContactQueries>();
        services.AddScoped<IContactInteractionLedger, EfContactInteractionLedger>();
        services.AddScoped<ProjectContactInteractionUseCase>();

        return services;
    }
}

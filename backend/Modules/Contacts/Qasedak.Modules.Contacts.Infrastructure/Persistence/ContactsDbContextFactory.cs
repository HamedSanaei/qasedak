using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Qasedak.Modules.Contacts.Infrastructure.Persistence;

namespace Qasedak.Modules.Contacts.Infrastructure;

/// <summary>Design-time factory: `dotnet ef` against the module's connection string.</summary>
public sealed class ContactsDbContextFactory : IDesignTimeDbContextFactory<ContactsDbContext>
{
    public ContactsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("QASEDAK_CONTACTS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=qasedak;Username=postgres;Password=postgres";

        return new ContactsDbContext(
            new DbContextOptionsBuilder<ContactsDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ContactsDbContext.Schema))
                .Options);
    }
}

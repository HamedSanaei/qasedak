using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Qasedak.Modules.Conversations.Infrastructure.Persistence;

namespace Qasedak.Modules.Conversations.Infrastructure;

/// <summary>Design-time factory: `dotnet ef` against the module's connection string.</summary>
public sealed class ConversationsDbContextFactory : IDesignTimeDbContextFactory<ConversationsDbContext>
{
    public ConversationsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("QASEDAK_CONVERSATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=qasedak;Username=postgres;Password=postgres";

        return new ConversationsDbContext(
            new DbContextOptionsBuilder<ConversationsDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ConversationsDbContext.Schema))
                .Options);
    }
}

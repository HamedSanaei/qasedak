using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qasedak.BuildingBlocks.Infrastructure.Auditing;
using Qasedak.Modules.Automations.Infrastructure.Persistence;
using Qasedak.Modules.Billing.Infrastructure.Persistence;
using Qasedak.Modules.Contacts.Infrastructure.Persistence;
using Qasedak.Modules.Conversations.Infrastructure.Persistence;
using Qasedak.Modules.Identity.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;

namespace Qasedak.Api.Migrations;

/// <summary>
/// One-shot production migration runner. Invoked with `dotnet Qasedak.Api.dll --migrate`
/// using the SAME release image as the API so the production host never needs `dotnet ef`
/// or the SDK. Each module DbContext migrates independently under its own schema; audit is
/// migrated only when its connection string is configured (Program.cs binds it conditionally).
/// Idempotent: an already-migrated schema is reported and skipped, never re-applied.
/// </summary>
public static class DatabaseMigrator
{
    public static async Task<int> MigrateAsync(IServiceProvider services, ILogger logger)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var contexts = new (string Label, DbContext? Context)[]
        {
            ("identity", sp.GetService<IdentityDbContext>()),
            ("instagram", sp.GetService<InstagramDbContext>()),
            ("conversations", sp.GetService<ConversationsDbContext>()),
            ("automations", sp.GetService<AutomationsDbContext>()),
            ("contacts", sp.GetService<ContactsDbContext>()),
            ("billing", sp.GetService<BillingDbContext>()),
            ("audit", sp.GetService<AuditDbContext>()),
        };

        foreach (var (label, context) in contexts)
        {
            if (context is null)
            {
                MigrationLogs.MissingRequiredSchema(logger, label);
                return 1;
            }

            try
            {
                var pending = context.Database.GetPendingMigrations().Cast<string>().ToList();
                if (pending.Count == 0)
                {
                    MigrationLogs.AlreadyUpToDate(logger, label);
                    continue;
                }

                MigrationLogs.Applying(logger, pending.Count, label);
                await context.Database.MigrateAsync(CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Do not log exception text: provider diagnostics can contain connection
                // details. The process still returns non-zero and the deployment aborts.
                MigrationLogs.Failed(logger, label);
                return 1;
            }
        }

        return 0;
    }
}

internal static partial class MigrationLogs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Applying {Count} migration(s) to schema '{Schema}'.")]
    public static partial void Applying(ILogger logger, int count, string schema);

    [LoggerMessage(Level = LogLevel.Information, Message = "Schema '{Schema}' is already up to date.")]
    public static partial void AlreadyUpToDate(ILogger logger, string schema);

    [LoggerMessage(Level = LogLevel.Error, Message = "Required schema '{Schema}' is not configured.")]
    public static partial void MissingRequiredSchema(ILogger logger, string schema);

    [LoggerMessage(Level = LogLevel.Error, Message = "Migration failed for schema '{Schema}'.")]
    public static partial void Failed(ILogger logger, string schema);
}

internal static partial class MigrationRunLogs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Starting one-shot database migration run.")]
    public static partial void Starting(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Migration run complete.")]
    public static partial void Completed(ILogger logger);
}

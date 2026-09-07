using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Application.HistorySync;

namespace Qasedak.Modules.Instagram.Infrastructure.HistorySync;

/// <summary>
/// M13-013 Phase B startup bootstrap: on every host start, ensures ACTIVE accounts
/// (including accounts that predate M13-013) acquire an initial bounded
/// conversation-history sync operation. One active operation per account+kind is
/// DB-enforced, so repeats/restarts/multi-instance collapse onto one logical sync.
/// DB-only: zero provider calls, zero token reads, no lock held during network I/O,
/// bounded per run (anti-thundering-herd), never schedules disconnected accounts.
/// </summary>
public sealed partial class ConversationHistorySyncScheduleBootstrap(
    IServiceScopeFactory scopeFactory,
    ILogger<ConversationHistorySyncScheduleBootstrap> logger) : IHostedService
{
    /// <summary>Bounded number of accounts ensured per bootstrap invocation.</summary>
    public const int MaxAccountsPerRun = 10;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var accounts = scope.ServiceProvider.GetRequiredService<IConnectedAccountRepository>();
            var ensure = scope.ServiceProvider.GetRequiredService<EnsureConversationSyncUseCase>();

            var active = accounts.ListActiveAsync(MaxAccountsPerRun, cancellationToken).GetAwaiter().GetResult();
            var enqueued = 0;
            foreach (var account in active)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = ensure.ExecuteAsync(account.Id, account.WorkspaceId, SyncOperationKind.Initial, cancellationToken).GetAwaiter().GetResult();
                if (!result.AlreadyActive)
                {
                    enqueued++;
                }
            }

            LogBootstrapResult(active.Count, enqueued);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Startup must never fail because sync scheduling could not be ensured:
            // existing operations keep running; the next host start retries.
            LogBootstrapFailed(exception.GetType().Name);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Conversation history bootstrap: active={Active} ensured={Ensured}.")]
    private partial void LogBootstrapResult(int active, int ensured);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Conversation history bootstrap failed ({Failure}); next host start retries.")]
    private partial void LogBootstrapFailed(string failure);
}

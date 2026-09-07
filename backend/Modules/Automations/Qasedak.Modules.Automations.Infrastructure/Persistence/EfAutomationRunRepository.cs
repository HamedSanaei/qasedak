using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;

namespace Qasedak.Modules.Automations.Infrastructure.Persistence;

/// <summary>Idempotency ledger over real PostgreSQL; unique (automation, trigger event).</summary>
public sealed class EfAutomationRunRepository(AutomationsDbContext context) : IAutomationRunRepository
{
    public async Task<AutomationRun?> FindByTriggerEventAsync(Guid automationId, string triggerEventId, CancellationToken cancellationToken = default)
    {
        // No-tracking: the aggregate is rebuilt from durable state on every load so a
        // direct DB transition (attempt marker CAS) can never be masked by a stale
        // tracked entity in the same scope.
        var row = await context.AutomationRuns.AsNoTracking()
            .Include(r => r.Actions.OrderBy(a => a.ActionIndex))
            .SingleOrDefaultAsync(r => r.AutomationId == automationId && r.TriggerEventId == triggerEventId, cancellationToken);
        return row is null ? null : FromRow(row);
    }

    public async Task<AutomationRun?> FindByIdAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        // No-tracking: see FindByTriggerEventAsync — the follow-up handler marks the slot
        // via a guarded UPDATE on the same DbContext and must see the fresh state after.
        var row = await context.AutomationRuns.AsNoTracking()
            .Include(r => r.Actions.OrderBy(a => a.ActionIndex))
            .SingleOrDefaultAsync(r => r.Id == runId, cancellationToken);
        return row is null ? null : FromRow(row);
    }

    public async Task<AttemptMarkerResult> MarkAttemptingAsync(
        Guid runId,
        int actionIndex,
        DateTimeOffset attemptedAtUtc,
        CancellationToken cancellationToken = default)
    {
        // Single guarded UPDATE: exactly one concurrent worker transitions Scheduled →
        // Attempting; PostgreSQL row locks make the second updater re-check the guard.
        var updated = await context.AutomationRunActions
            .Where(a => a.RunId == runId && a.ActionIndex == actionIndex && a.Status == AutomationActionStatus.Scheduled)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, AutomationActionStatus.Attempting)
                .SetProperty(a => a.AttemptedAtUtc, attemptedAtUtc), cancellationToken);

        if (updated == 1)
        {
            return AttemptMarkerResult.Transitioned;
        }

        var current = await context.AutomationRunActions.AsNoTracking()
            .Where(a => a.RunId == runId && a.ActionIndex == actionIndex)
            .Select(a => (AutomationActionStatus?)a.Status)
            .SingleOrDefaultAsync(cancellationToken);

        return current switch
        {
            null => AttemptMarkerResult.NotFound,
            AutomationActionStatus.Attempting => AttemptMarkerResult.AlreadyAttempting,
            AutomationActionStatus.Succeeded or
                AutomationActionStatus.Suppressed or
                AutomationActionStatus.Uncertain or
                AutomationActionStatus.TerminalFailed => AttemptMarkerResult.AlreadySettled,
            _ => AttemptMarkerResult.SlotNotReady,
        };
    }

    public async Task<AttemptMarkerResult> SuppressAsync(
        Guid runId,
        int actionIndex,
        string failureCode,
        DateTimeOffset completedAtUtc,
        AutomationActionStatus status = AutomationActionStatus.Suppressed,
        CancellationToken cancellationToken = default)
    {
        // Pre-send terminal verdict: guarded on Scheduled OR Attempting so a concurrent
        // marker winner is never raced into a duplicate send; a concurrent suppressor
        // with the same verdict is idempotent.
        var updated = await context.AutomationRunActions
            .Where(a => a.RunId == runId && a.ActionIndex == actionIndex
                && (a.Status == AutomationActionStatus.Scheduled || a.Status == AutomationActionStatus.Attempting))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, status)
                .SetProperty(a => a.FailureCode, failureCode)
                .SetProperty(a => a.CompletedAtUtc, completedAtUtc), cancellationToken);

        if (updated == 1)
        {
            return AttemptMarkerResult.Transitioned;
        }

        var current = await context.AutomationRunActions.AsNoTracking()
            .Where(a => a.RunId == runId && a.ActionIndex == actionIndex)
            .Select(a => (AutomationActionStatus?)a.Status)
            .SingleOrDefaultAsync(cancellationToken);

        return current switch
        {
            null => AttemptMarkerResult.NotFound,
            AutomationActionStatus.Succeeded or
                AutomationActionStatus.Suppressed or
                AutomationActionStatus.Uncertain or
                AutomationActionStatus.TerminalFailed => AttemptMarkerResult.AlreadySettled,
            _ => AttemptMarkerResult.SlotNotReady,
        };
    }

    public async Task SaveChangesAsync(AutomationRun run, CancellationToken cancellationToken = default)
    {
        // Upsert semantics: locals first (inserts within this scope), then the database.
        // The aggregate is the only source of truth; rows are rebuilt from it.
        var row = context.AutomationRuns.Local.FirstOrDefault(r => r.Id == run.Id)
            ?? await context.AutomationRuns
                .Include(r => r.Actions)
                .FirstOrDefaultAsync(r => r.Id == run.Id, cancellationToken);

        if (row is null)
        {
            context.AutomationRuns.Add(ToRow(run));
        }
        else
        {
            row.Status = run.Status;
            row.FinishedAtUtc = run.FinishedAtUtc;

            // Slots are fixed at start; merge state in place instead of replacing children
            // (re-adding tracked keys trips the identity map).
            foreach (var action in run.Actions)
            {
                var tracked = row.Actions.FirstOrDefault(x => x.ActionIndex == action.Index);
                if (tracked is null)
                {
                    row.Actions.Add(new AutomationRunActionRow
                    {
                        RunId = run.Id,
                        ActionIndex = action.Index,
                        Status = action.Status,
                        FailureCode = action.FailureCode,
                        AttemptedAtUtc = action.AttemptedAtUtc,
                        CompletedAtUtc = action.CompletedAtUtc,
                        ProviderRecipientId = action.ProviderRecipientId,
                        ProviderMessageId = action.ProviderMessageId,
                    });
                }
                else
                {
                    tracked.Status = action.Status;
                    tracked.FailureCode = action.FailureCode;
                    tracked.AttemptedAtUtc = action.AttemptedAtUtc;
                    tracked.CompletedAtUtc = action.CompletedAtUtc;
                    tracked.ProviderRecipientId = action.ProviderRecipientId;
                    tracked.ProviderMessageId = action.ProviderMessageId;
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static AutomationRun FromRow(AutomationRunRow row) => AutomationRun.FromState(
        row.Id,
        row.AutomationId,
        row.AutomationVersionNumber,
        row.TriggerEventId,
        row.WorkspaceId,
        row.Status,
        row.StartedAtUtc,
        row.FinishedAtUtc,
        row.Actions.OrderBy(a => a.ActionIndex)
            .Select(a => new AutomationActionExecution(
                a.ActionIndex, a.Status, a.FailureCode, a.AttemptedAtUtc, a.CompletedAtUtc, a.ProviderRecipientId, a.ProviderMessageId))
            .ToList());

    private static AutomationRunRow ToRow(AutomationRun run) => new()
    {
        Id = run.Id,
        AutomationId = run.AutomationId,
        AutomationVersionNumber = run.AutomationVersionNumber,
        TriggerEventId = run.TriggerEventId,
        WorkspaceId = run.WorkspaceId,
        Status = run.Status,
        StartedAtUtc = run.StartedAtUtc,
        FinishedAtUtc = run.FinishedAtUtc,
        Actions = run.Actions
            .Select(a => new AutomationRunActionRow
            {
                RunId = run.Id,
                ActionIndex = a.Index,
                Status = a.Status,
                FailureCode = a.FailureCode,
                AttemptedAtUtc = a.AttemptedAtUtc,
                CompletedAtUtc = a.CompletedAtUtc,
                ProviderRecipientId = a.ProviderRecipientId,
                ProviderMessageId = a.ProviderMessageId,
            })
            .ToList(),
    };
}

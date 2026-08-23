using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;

namespace Qasedak.Modules.Automations.Infrastructure.Persistence;

/// <summary>Idempotency ledger over real PostgreSQL; unique (automation, trigger event).</summary>
public sealed class EfAutomationRunRepository(AutomationsDbContext context) : IAutomationRunRepository
{
    public async Task<AutomationRun?> FindByTriggerEventAsync(Guid automationId, string triggerEventId, CancellationToken cancellationToken = default)
    {
        var row = await context.AutomationRuns
            .Include(r => r.Actions.OrderBy(a => a.ActionIndex))
            .SingleOrDefaultAsync(r => r.AutomationId == automationId && r.TriggerEventId == triggerEventId, cancellationToken);
        return row is null ? null : FromRow(row);
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
                    });
                }
                else
                {
                    tracked.Status = action.Status;
                    tracked.FailureCode = action.FailureCode;
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
            .Select(a => new AutomationActionExecution(a.ActionIndex, a.Status, a.FailureCode))
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
            .Select(a => new AutomationRunActionRow { RunId = run.Id, ActionIndex = a.Index, Status = a.Status, FailureCode = a.FailureCode })
            .ToList(),
    };
}

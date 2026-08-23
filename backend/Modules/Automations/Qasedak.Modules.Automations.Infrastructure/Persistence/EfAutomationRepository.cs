using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;

namespace Qasedak.Modules.Automations.Infrastructure.Persistence;

/// <summary>Loads/saves aggregates with their immutable version history.</summary>
public sealed class EfAutomationRepository(AutomationsDbContext context) : IAutomationRepository
{
    public async Task<Automation?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await context.Automations
            .Include(r => r.Versions.OrderBy(v => v.Number))
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        return row is null ? null : FromRow(row);
    }

    public async Task<IReadOnlyList<Automation>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var rows = await context.Automations
            .Include(r => r.Versions.OrderBy(v => v.Number))
            .Where(r => r.WorkspaceId == workspaceId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return rows.Select(FromRow).ToList();
    }

    public async Task SaveChangesAsync(Automation automation, CancellationToken cancellationToken = default)
    {
        // Upsert semantics: check tracked locals first (covers inserts within this scope),
        // then the database.
        var row = context.Automations.Local.FirstOrDefault(r => r.Id == automation.Id)
            ?? await context.Automations
                .Include(r => r.Versions)
                .FirstOrDefaultAsync(r => r.Id == automation.Id, cancellationToken);

        if (row is null)
        {
            context.Automations.Add(ToRow(automation));
        }
        else
        {
            row.Status = automation.Status;
            row.ActivatedAtUtc = automation.ActivatedAtUtc;
            row.DisabledAtUtc = automation.DisabledAtUtc;
            row.CurrentVersionFrozen = automation.CurrentVersionFrozen;

            // Versions are append-only; merge by number in place (re-adding tracked keys
            // trips the identity map).
            foreach (var version in automation.Versions)
            {
                var tracked = row.Versions.FirstOrDefault(v => v.Number == version.Number);
                if (tracked is null)
                {
                    row.Versions.Add(new AutomationVersionRow
                    {
                        AutomationId = automation.Id,
                        Number = version.Number,
                        DefinitionJson = AutomationDefinitionSerializer.Serialize(version.Definition),
                        CreatedAtUtc = version.CreatedAtUtc,
                    });
                }
                else
                {
                    // Stored versions are immutable by contract; nothing to update.
                }
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static Automation FromRow(AutomationRow row) => Automation.FromState(
        row.Id,
        row.WorkspaceId,
        row.Name,
        row.Status,
        row.CreatedAtUtc,
        row.ActivatedAtUtc,
        row.DisabledAtUtc,
        row.Versions
            .OrderBy(v => v.Number)
            .Select(v => new AutomationVersion(
                v.Number,
                AutomationDefinitionSerializer.Deserialize(v.DefinitionJson),
                v.CreatedAtUtc))
            .ToList(),
        row.CurrentVersionFrozen);

    private static AutomationRow ToRow(Automation automation) => new()
    {
        Id = automation.Id,
        WorkspaceId = automation.WorkspaceId,
        Name = automation.Name,
        Status = automation.Status,
        CreatedAtUtc = automation.CreatedAtUtc,
        ActivatedAtUtc = automation.ActivatedAtUtc,
        DisabledAtUtc = automation.DisabledAtUtc,
        CurrentVersionFrozen = automation.CurrentVersionFrozen,
        Versions = automation.Versions
            .Select(v => new AutomationVersionRow
            {
                AutomationId = automation.Id,
                Number = v.Number,
                DefinitionJson = AutomationDefinitionSerializer.Serialize(v.Definition),
                CreatedAtUtc = v.CreatedAtUtc,
            })
            .ToList(),
    };
}

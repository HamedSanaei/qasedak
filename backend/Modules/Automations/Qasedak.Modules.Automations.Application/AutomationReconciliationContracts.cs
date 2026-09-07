namespace Qasedak.Modules.Automations.Application;

/// <summary>
/// Channel-neutral reconciliation scope (M13-013 §13): what the Instagram module needs
/// to decide which media require comment-history reconciliation for ONE exact account.
/// Deliberately contains NO automation message text, actions, names or definitions —
/// only the media scope. <c>null</c> (from the repository method) means the exact
/// account has no active comment-trigger automation at all.
/// </summary>
public sealed record AutomationReconciliationScope(
    bool HasAnySourceCommentAutomation,
    IReadOnlyList<string> SpecificSourceMediaIds)
{
    public static AutomationReconciliationScope None() => new(false, []);
}

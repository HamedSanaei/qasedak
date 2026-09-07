namespace Qasedak.Modules.Instagram.Application.HistorySync;

/// <summary>
/// Channel-neutral provider-history import gateway (M13-013 §50). Implemented at the
/// composition root, which maps <see cref="HistoryMessageImport"/> rows into the
/// Conversations module's import/upsert contract. Instagram Infrastructure never
/// writes the conversations schema. Import semantics are EXPLICITLY not a webhook:
/// no unread inflation, no automation fan-out — provider history is not a
/// new-message notification.
/// </summary>
public interface IConversationHistoryImportGateway
{
    /// <summary>Imports one bounded history message. Returns true when a NEW row was
    /// created (false = duplicate/older-preserved no-op).</summary>
    Task<bool> ImportAsync(HistoryMessageImport message, CancellationToken cancellationToken = default);
}

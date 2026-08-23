using Qasedak.Modules.Conversations.Application.Conversations;
using Qasedak.Modules.Instagram.Application.Webhooks;

namespace Qasedak.Api.CrossModule;

/// <summary>
/// Fills Instagram's post-ingest seam: pending webhook entries are normalized and
/// dispatched (which routes messaging events into the Conversations inbox through the
/// registered integration-event dispatcher).
/// </summary>
public sealed class ConversationsPostIngestAdapter(ProcessPendingWebhookEventsUseCase processor) : IWebhookPostIngestProcessor
{
    public Task ProcessPendingAsync(int maxEntries = 50, CancellationToken cancellationToken = default) =>
        processor.ProcessPendingAsync(maxEntries, cancellationToken);
}

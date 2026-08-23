using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

public sealed class ProcessPendingWebhookEventsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 19, 0, 0, TimeSpan.Zero);

    private sealed class RecordingInbox(params InboxEntryRecord[] pending) : IWebhookInboxStore
    {
        public List<string> Processed { get; } = [];

        public List<IIntegrationEvent> Dispatched { get; } = [];

        public Task<InboxEntryRecord?> LoadAsync(string eventId, CancellationToken cancellationToken = default) =>
            Task.FromResult<InboxEntryRecord?>(null);

        public Task<IReadOnlyList<InboxEntryRecord>> ListPendingAsync(int maxEntries, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<InboxEntryRecord> entries = pending.Take(maxEntries).ToArray();
            return Task.FromResult(entries);
        }

        public Task MarkProcessedAsync(string eventId, DateTimeOffset processedAtUtc, CancellationToken cancellationToken = default)
        {
            Processed.Add(eventId);
            return Task.CompletedTask;
        }

        public Task RecordDeliveryAttemptAsync(string eventId, DateTimeOffset atUtc, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<int> CountPendingAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(pending.Length);
        }

        public bool EveryDispatchedEventWasProcessed =>
            Dispatched.All(e => Processed.Contains(e.EventId));
    }

    private sealed class RecordingDispatcher : IIntegrationEventDispatcher
    {
        public List<IIntegrationEvent> Events { get; } = [];

        public Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(integrationEvent);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PendingEntriesAreNormalizedDispatchedAndClosed()
    {
        var inbox = new RecordingInbox(
            new InboxEntryRecord("evt-a", "instagram",
                """{"entry":[{"id":"17841400000000000","messaging":[{"sender":{"id":"u1"},"timestamp":1771800100,"message":{"mid":"m","text":"hi"}}]}]}"""),
            new InboxEntryRecord("evt-b", "instagram",
                """{"entry":[{"id":"17841400000000000","changes":[{"field":"comments","value":{"id":"c1","text":"ok","created_time":1771900000}}]}]}"""));
        var dispatcher = new RecordingDispatcher();
        var useCase = new ProcessPendingWebhookEventsUseCase(inbox, dispatcher, new FixedClock(Now));

        var summary = await useCase.ProcessPendingAsync();

        Assert.Equal(2, summary.Inspected);
        Assert.Equal(2, summary.Processed);
        Assert.Equal(2, dispatcher.Events.Count);
        Assert.Equal(["evt-a", "evt-b"], inbox.Processed);
        Assert.True(inbox.EveryDispatchedEventWasProcessed);
    }

    [Fact]
    public async Task UnrecognizedFragmentsDoNotBlockClosing()
    {
        var inbox = new RecordingInbox(
            new InboxEntryRecord("evt-c", "instagram", """{"entry":[{"id":"x","changes":[{"field":"story_insights","value":{}}]}]}"""));
        var dispatcher = new RecordingDispatcher();
        var useCase = new ProcessPendingWebhookEventsUseCase(inbox, dispatcher, new FixedClock(Now));

        var summary = await useCase.ProcessPendingAsync();

        Assert.Equal(1, summary.Inspected);
        Assert.Equal(1, summary.Processed);
        Assert.Equal(1, summary.UnrecognizedFragments);
        Assert.Empty(dispatcher.Events);
        Assert.Contains("evt-c", inbox.Processed);
    }

    [Fact]
    public async Task BatchSizeIsRespected()
    {
        var inbox = new RecordingInbox(
            Enumerable.Range(0, 10)
                .Select(i => new InboxEntryRecord($"evt-{i}", "instagram", "{}"))
                .ToArray());
        var dispatcher = new RecordingDispatcher();
        var useCase = new ProcessPendingWebhookEventsUseCase(inbox, dispatcher, new FixedClock(Now));

        var summary = await useCase.ProcessPendingAsync(maxEntries: 3);

        Assert.Equal(3, summary.Inspected);
        Assert.Equal(3, inbox.Processed.Count);
    }
}


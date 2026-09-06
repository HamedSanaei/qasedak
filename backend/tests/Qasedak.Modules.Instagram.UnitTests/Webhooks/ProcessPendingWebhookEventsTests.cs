using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Application.Webhooks;
using Qasedak.Modules.Instagram.UnitTests.TestSupport;
using Xunit;

namespace Qasedak.Modules.Instagram.UnitTests;

public sealed class ProcessPendingWebhookEventsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 19, 0, 0, TimeSpan.Zero);

    private static readonly Guid WorkspaceA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid WorkspaceB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid AccountA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AccountB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class RecordingInbox(params InboxEntryRecord[] pending) : IWebhookInboxStore
    {
        public List<string> Processed { get; } = [];

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

        public Task RecordDeliveryAttemptAsync(string eventId, DateTimeOffset atUtc, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<int> CountPendingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(pending.Length);
    }

    private sealed class RecordingDispatcher : IIntegrationEventDispatcher
    {
        public List<IIntegrationEvent> Events { get; } = [];

        public Func<CancellationToken, Task>? OnDispatch { get; set; }

        public Task DispatchAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(integrationEvent);
            return OnDispatch is null ? Task.CompletedTask : OnDispatch(cancellationToken);
        }
    }

    /// <summary>Deterministic account-resolution fake; ResolveAsync calls are recorded per provider id.</summary>
    private sealed class FakeResolver : IInboundAccountResolver
    {
        private readonly Dictionary<string, InboundAccountResolution> _resolutions;

        public List<string> Calls { get; } = [];

        public FakeResolver(Dictionary<string, InboundAccountResolution> resolutions) => _resolutions = resolutions;

        public Task<InboundAccountResolution> ResolveAsync(string? providerAccountId, CancellationToken cancellationToken = default)
        {
            Calls.Add(providerAccountId ?? "(null)");
            return Task.FromResult(
                providerAccountId is not null && _resolutions.TryGetValue(providerAccountId, out var resolution)
                    ? resolution
                    : InboundAccountResolution.NotFound());
        }
    }

    private sealed class RecordingObservability : IWebhookNormalizationObservability
    {
        public List<string> Normalized { get; } = [];

        public List<string> Ignored { get; } = [];

        public List<string> Unrecognized { get; } = [];

        public void RecordNormalized(string kind) => Normalized.Add(kind);

        public void RecordIgnored(string reason) => Ignored.Add(reason);

        public void RecordUnrecognized(string kind) => Unrecognized.Add(kind);
    }

    private static ProcessPendingWebhookEventsUseCase NewUseCase(
        RecordingInbox inbox,
        RecordingDispatcher dispatcher,
        IInboundAccountResolver resolver,
        RecordingObservability observability) =>
        new(inbox, dispatcher, new FixedClock(Now), resolver, observability);

    private static InboxEntryRecord MessageEntry(string eventId, string providerAccountId = "provider-A", string mid = "m") =>
        new(eventId, "instagram",
            "{\"entry\":[{\"id\":\"" + providerAccountId + "\",\"time\":1502905976963,\"messaging\":[{\"sender\":{\"id\":\"u1\"},\"timestamp\":1502905976377,\"message\":{\"mid\":\"" + mid + "\",\"text\":\"hi\"}}]}]}");

    [Fact]
    public async Task PendingEntriesAreResolvedEnrichedDispatchedAndClosed()
    {
        var inbox = new RecordingInbox(
            MessageEntry("evt-a", "provider-A", "m-a"),
            new InboxEntryRecord("evt-b", "instagram",
                """{"entry":[{"id":"provider-A","time":1502905976963,"changes":[{"field":"comments","value":{"id":"c1","text":"ok"}}]}]}"""));
        var dispatcher = new RecordingDispatcher();
        var resolver = new FakeResolver(new Dictionary<string, InboundAccountResolution>
        {
            ["provider-A"] = InboundAccountResolution.Resolved(WorkspaceA, AccountA),
        });
        var observability = new RecordingObservability();
        var useCase = NewUseCase(inbox, dispatcher, resolver, observability);

        var summary = await useCase.ProcessPendingAsync();

        Assert.Equal(2, summary.Inspected);
        Assert.Equal(2, summary.Processed);
        Assert.Equal(2, dispatcher.Events.Count);
        Assert.Equal(["evt-a", "evt-b"], inbox.Processed);

        // Enrichment: every event carries the exact resolved account key + workspace.
        Assert.All(dispatcher.Events, e =>
        {
            Assert.Equal(WorkspaceA, e.WorkspaceId);
            Assert.Equal(AccountA, e.ConnectedAccountId);
        });
        Assert.Equal(["message", "comment"], observability.Normalized);
    }

    [Fact]
    public async Task MultiEntryFanOutResolvesEachEntryIndependently()
    {
        var inbox = new RecordingInbox(new InboxEntryRecord("evt-fanout", "instagram",
            """{"entry":[{"id":"provider-A","time":1502905976963,"messaging":[{"sender":{"id":"u1"},"timestamp":1502905976377,"message":{"mid":"m-a","text":"for A"}}]},{"id":"provider-B","time":1502905976963,"changes":[{"field":"comments","value":{"id":"c-b","text":"for B"}}]}]}"""));
        var dispatcher = new RecordingDispatcher();
        var resolver = new FakeResolver(new Dictionary<string, InboundAccountResolution>
        {
            ["provider-A"] = InboundAccountResolution.Resolved(WorkspaceA, AccountA),
            ["provider-B"] = InboundAccountResolution.Resolved(WorkspaceB, AccountB),
        });
        var useCase = NewUseCase(inbox, dispatcher, resolver, new RecordingObservability());

        await useCase.ProcessPendingAsync();

        Assert.Equal(["provider-A", "provider-B"], resolver.Calls);
        var events = dispatcher.Events;
        Assert.Equal(2, events.Count);
        // Entry A's event must carry A's account; entry B's event must carry B's — never
        // a reuse of the first resolved account.
        Assert.Equal(AccountA, events[0].ConnectedAccountId);
        Assert.Equal(WorkspaceA, events[0].WorkspaceId);
        Assert.Equal(AccountB, events[1].ConnectedAccountId);
        Assert.Equal(WorkspaceB, events[1].WorkspaceId);
        Assert.NotEqual(events[0].EventId, events[1].EventId);
    }

    [Fact]
    public async Task UnresolvedAccountDispatchesNothingAndClosesWithObservability()
    {
        var inbox = new RecordingInbox(MessageEntry("evt-unknown", "provider-unknown"));
        var dispatcher = new RecordingDispatcher();
        var resolver = new FakeResolver(new Dictionary<string, InboundAccountResolution>());
        var observability = new RecordingObservability();
        var useCase = NewUseCase(inbox, dispatcher, resolver, observability);

        var summary = await useCase.ProcessPendingAsync();

        Assert.Empty(dispatcher.Events);
        Assert.Contains("evt-unknown", inbox.Processed);
        Assert.Equal(1, summary.UnrecognizedFragments);
        Assert.Equal(["unresolved-account"], observability.Unrecognized);
    }

    [Fact]
    public async Task AmbiguousAccountFailsClosedWithZeroDispatch()
    {
        var inbox = new RecordingInbox(MessageEntry("evt-ambiguous", "provider-ambiguous"));
        var dispatcher = new RecordingDispatcher();
        var resolver = new FakeResolver(new Dictionary<string, InboundAccountResolution>
        {
            ["provider-ambiguous"] = InboundAccountResolution.Ambiguous(),
        });
        var observability = new RecordingObservability();
        var useCase = NewUseCase(inbox, dispatcher, resolver, observability);

        var summary = await useCase.ProcessPendingAsync();

        Assert.Empty(dispatcher.Events);
        Assert.Contains("evt-ambiguous", inbox.Processed);
        Assert.Equal(1, summary.UnrecognizedFragments);
        Assert.Equal(["ambiguous-account"], observability.Unrecognized);
    }

    [Fact]
    public async Task EntryWithoutProviderIdResolvesAsNotFoundWithoutGuessing()
    {
        var inbox = new RecordingInbox(new InboxEntryRecord("evt-no-id", "instagram",
            """{"entry":[{"time":1502905976963,"messaging":[{"sender":{"id":"u1"},"timestamp":1502905976377,"message":{"mid":"m","text":"hi"}}]}]}"""));
        var dispatcher = new RecordingDispatcher();
        var resolver = new FakeResolver(new Dictionary<string, InboundAccountResolution>());
        var useCase = NewUseCase(inbox, dispatcher, resolver, new RecordingObservability());

        await useCase.ProcessPendingAsync();

        Assert.Equal(["(null)"], resolver.Calls);
        Assert.Empty(dispatcher.Events);
        Assert.Contains("evt-no-id", inbox.Processed);
    }

    [Fact]
    public async Task UnrecognizedAndIgnoredFragmentsDoNotBlockClosing()
    {
        var inbox = new RecordingInbox(new InboxEntryRecord("evt-c", "instagram",
            """{"entry":[{"id":"x","time":1502905976963,"changes":[{"field":"story_insights","value":{}}],"messaging":[{"sender":{"id":"u1"},"timestamp":1502905976377,"message":{"is_echo":true,"mid":"m"}}]}]}"""));
        var dispatcher = new RecordingDispatcher();
        var resolver = new FakeResolver(new Dictionary<string, InboundAccountResolution>
        {
            ["x"] = InboundAccountResolution.Resolved(WorkspaceA, AccountA),
        });
        var observability = new RecordingObservability();
        var useCase = NewUseCase(inbox, dispatcher, resolver, observability);

        var summary = await useCase.ProcessPendingAsync();

        Assert.Equal(1, summary.Inspected);
        Assert.Equal(1, summary.Processed);
        Assert.Equal(1, summary.UnrecognizedFragments);
        Assert.Equal(1, summary.IgnoredFragments);
        Assert.Empty(dispatcher.Events);
        Assert.Contains("evt-c", inbox.Processed);
        Assert.Equal(["field:story_insights"], observability.Unrecognized);
        Assert.Equal(["message-echo"], observability.Ignored);
    }

    [Fact]
    public async Task BatchSizeIsRespected()
    {
        var inbox = new RecordingInbox(
            Enumerable.Range(0, 10)
                .Select(i => new InboxEntryRecord($"evt-{i}", "instagram", "{}"))
                .ToArray());
        var dispatcher = new RecordingDispatcher();
        var useCase = NewUseCase(inbox, dispatcher, new FakeResolver([]), new RecordingObservability());

        var summary = await useCase.ProcessPendingAsync(maxEntries: 3);

        Assert.Equal(3, summary.Inspected);
        Assert.Equal(3, inbox.Processed.Count);
    }

    [Fact]
    public async Task DispatchFailureLeavesEntryPendingForRetry()
    {
        var inbox = new RecordingInbox(MessageEntry("evt-fail"));
        var dispatcher = new RecordingDispatcher { OnDispatch = _ => throw new InvalidOperationException("consumer blew up") };
        var resolver = new FakeResolver(new Dictionary<string, InboundAccountResolution>
        {
            ["provider-A"] = InboundAccountResolution.Resolved(WorkspaceA, AccountA),
        });
        var useCase = NewUseCase(inbox, dispatcher, resolver, new RecordingObservability());

        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ProcessPendingAsync());

        Assert.Empty(inbox.Processed);
        Assert.Single(dispatcher.Events);
    }

    [Fact]
    public async Task CancellationBetweenNormalizationAndDispatchLeavesEntryPending()
    {
        var inbox = new RecordingInbox(MessageEntry("evt-cancel"), MessageEntry("evt-later"));
        var dispatcher = new RecordingDispatcher
        {
            OnDispatch = _ => throw new OperationCanceledException(),
        };
        var resolver = new FakeResolver(new Dictionary<string, InboundAccountResolution>
        {
            ["provider-A"] = InboundAccountResolution.Resolved(WorkspaceA, AccountA),
        });
        var useCase = NewUseCase(inbox, dispatcher, resolver, new RecordingObservability());

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useCase.ProcessPendingAsync(cancellationToken: cts.Token));

        // No entry was marked complete after cancellation between normalization and dispatch.
        Assert.Empty(inbox.Processed);
    }

    [Fact]
    public async Task CancellationDuringResolutionLeavesEntryPending()
    {
        var inbox = new RecordingInbox(MessageEntry("evt-cancel-resolve"));
        var dispatcher = new RecordingDispatcher();
        var resolver = new ThrowingResolver(new OperationCanceledException());
        var useCase = NewUseCase(inbox, dispatcher, resolver, new RecordingObservability());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => useCase.ProcessPendingAsync());

        Assert.Empty(inbox.Processed);
        Assert.Empty(dispatcher.Events);
    }

    private sealed class ThrowingResolver(Exception exception) : IInboundAccountResolver
    {
        public Task<InboundAccountResolution> ResolveAsync(string? providerAccountId, CancellationToken cancellationToken = default) =>
            Task.FromException<InboundAccountResolution>(exception);
    }
}

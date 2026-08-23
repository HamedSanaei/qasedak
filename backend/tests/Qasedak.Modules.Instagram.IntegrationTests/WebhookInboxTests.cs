using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Qasedak.BuildingBlocks.Application;
using Qasedak.Modules.Instagram.Infrastructure.Persistence;
using Qasedak.Modules.Instagram.Infrastructure.Webhooks;
using Xunit;

namespace Qasedak.Modules.Instagram.IntegrationTests;

[Collection(PostgresTestEnvironment.Name)]
public sealed class WebhookInboxTests(PostgreSqlFixture fixture)
{
    private static readonly DateTimeOffset ReceivedAt = new(2026, 8, 23, 18, 30, 0, TimeSpan.Zero);

    private static string Body(string marker) =>
        $"{{\"object\":\"instagram\",\"entry\":[{{\"id\":\"17841400000000000\",\"time\":{marker}}}]}}";

    private InboxWebhookIngester NewIngester() =>
        new(new InstagramDbContext(new DbContextOptionsBuilder<InstagramDbContext>()
                .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", InstagramDbContext.Schema))
                .Options),
            new FixedClock(ReceivedAt),
            new WebhookMetrics(),
            NullLogger<InboxWebhookIngester>.Instance);

    private static Application.Webhooks.MetaWebhookNotification Notification(string body) =>
        new("instagram", body, "corr-test");

    [Fact]
    public async Task FirstDeliveryPersistsInboxEntryAsPending()
    {
        var ingester = NewIngester();
        var body = Body("1000");

        var result = await ingester.IngestAsync(
            Notification(body));

        Assert.True(result.Accepted);
        await using var verify = new InstagramDbContext(new DbContextOptionsBuilder<InstagramDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString()).Options);
        var stored = await verify.WebhookInbox.SingleAsync(e => e.BodyJson == body);
        Assert.Equal("pending", stored.Status);
        Assert.Equal(0, stored.DeliveryAttempts);
        Assert.Equal(64, stored.EventId.Length);
    }

    [Fact]
    public async Task RedeliveryOfIdenticalBodyIsASwallowedNoOp()
    {
        var ingester = NewIngester();
        var body = Body("2000");
        await ingester.IngestAsync(Notification(body));

        var second = await ingester.IngestAsync(Notification(body));

        Assert.True(second.Accepted);
        await using var verify = new InstagramDbContext(new DbContextOptionsBuilder<InstagramDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString()).Options);
        var rows = await verify.WebhookInbox.Where(e => e.BodyJson == body).ToListAsync();
        var stored = Assert.Single(rows);
        Assert.Equal(1, stored.DeliveryAttempts);
    }

    [Fact]
    public async Task DistinctPayloadsOccupyDistinctIdentities()
    {
        var ingester = NewIngester();

        await ingester.IngestAsync(Notification(Body("3001")));
        await ingester.IngestAsync(Notification(Body("3002")));

        await using var verify = new InstagramDbContext(new DbContextOptionsBuilder<InstagramDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString()).Options);
        Assert.Equal(2, await verify.WebhookInbox.CountAsync(e => e.Topic == "instagram" && e.BodyJson.Contains("300")));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}

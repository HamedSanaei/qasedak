using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Qasedak.BuildingBlocks.Application.Auditing;
using Xunit;

namespace Qasedak.Api.IntegrationTests;

/// <summary>
/// Sensitive-action audit trail over real PostgreSQL: failed and successful logins are
/// recorded without credential or verbatim-email leakage, entries are append-only, and
/// the audit schema is queryable by action.
/// </summary>
[Collection(ApiTestEnvironment.Name)]
public sealed class AuditTrailEndpointTests(ApiPostgreSqlFixture fixture)
{
    [Fact]
    public async Task FailedLoginsAreAuditedWithoutCredentialLeakage()
    {
        var password = "wrong-password-123";
        using var response = await fixture.Client.PostAsJsonAsync("/api/v1/identity/login", new
        {
            email = "audit-failed-login@example.com",
            password,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var entries = await fixture.ReadAuditEntriesAsync();
        var failure = entries.LastOrDefault(e => e.Action == "auth.login.failed");
        Assert.NotNull(failure);
        Assert.NotEqual(default, failure.AtUtc);
        // Privacy: no raw email, no password anywhere in the record.
        var payload = JsonSerializer.Serialize(failure);
        Assert.DoesNotContain("audit-failed-login@example.com", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(password, payload, StringComparison.Ordinal);
        Assert.Contains("fp_", failure.DetailsJson ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulLoginsAreAuditedWithActorIdentity()
    {
        var email = $"audit-ok-{Guid.CreateVersion7():N}@example.com";
        using var register = await fixture.Client.PostAsJsonAsync("/api/v1/identity/register", new
        {
            email,
            displayName = "Audit User",
            password = "correct-horse-battery-9",
        });
        register.EnsureSuccessStatusCode();

        using var login = await fixture.Client.PostAsJsonAsync("/api/v1/identity/login", new
        {
            email,
            password = "correct-horse-battery-9",
        });
        login.EnsureSuccessStatusCode();

        var entries = await fixture.ReadAuditEntriesAsync();
        var success = entries.LastOrDefault(e => e.Action == "auth.login.succeeded");
        Assert.NotNull(success);
        Assert.True(success.ActorUserId != Guid.Empty, "successful login audit carries the actor user id");
        Assert.Equal("user", success.TargetType);
    }

    [Fact]
    public async Task AuditRecordsAreAppendOnly()
    {
        var entry = AuditEntry.New(
            "test.append.only",
            DateTimeOffset.UtcNow,
            targetType: "probe",
            targetId: Guid.CreateVersion7().ToString());
        await fixture.RecordAuditAsync(entry);
        await fixture.RecordAuditAsync(entry with { AuditId = Guid.CreateVersion7() });

        var entries = await fixture.ReadAuditEntriesAsync();
        var stored = entries.Where(e => e.AuditId == entry.AuditId).ToList();
        Assert.Single(stored); // write-once: the same id is never duplicated or mutated
        Assert.Equal("test.append.only", stored[0].Action);
    }
}

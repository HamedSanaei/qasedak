using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Identity.Domain.Users;
using Qasedak.Modules.Identity.Domain.Workspaces;
using Qasedak.Modules.Identity.Infrastructure.Persistence;
using Xunit;

namespace Qasedak.Modules.Identity.IntegrationTests;

[Collection(PostgresTestEnvironment.Name)]
public sealed class IdentityPersistenceTests(PostgreSqlFixture fixture)
{
    private IdentityDbContext NewContext() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(fixture.Context.Database.GetConnectionString(), npgsql =>
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema))
            .Options);

    private static async Task CreateUserAsync(IdentityDbContext context, params Guid[] userIds)
    {
        foreach (var userId in userIds)
        {
            await context.Users.AddAsync(
                User.FromState(userId, EmailAddress.Create($"user-{userId:N}@example.com"), "Test User"));
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task UserAndCredentialRoundTripThroughRepository()
    {
        await using var context = NewContext();
        var users = new EfUserRepository(context);
        var user = User.Create(EmailAddress.Create("Persist@Example.COM "), "Persisted User");

        await users.AddAsync(user, "pbkdf2-sha256.210000.c2FsdA==.aGFzaA==");
        await users.SaveChangesAsync();

        context.ChangeTracker.Clear();
        var found = await users.FindByEmailAsync(EmailAddress.Create("persist@example.com"));
        var hash = await users.GetPasswordHashAsync(found!.Id);

        Assert.NotNull(found);
        Assert.Equal("persist@example.com", found.Email.Value);
        Assert.Equal("pbkdf2-sha256.210000.c2FsdA==.aGFzaA==", hash);
    }

    [Fact]
    public async Task DuplicateEmailViolatesUniqueIndex()
    {
        await using var context = NewContext();
        var users = new EfUserRepository(context);

        await users.AddAsync(User.Create(EmailAddress.Create("dupe@example.com"), "First"), "h1");
        await users.SaveChangesAsync();
        context.ChangeTracker.Clear();

        await users.AddAsync(User.Create(EmailAddress.Create("DUPE@example.com"), "Second"), "h2");

        await Assert.ThrowsAsync<DbUpdateException>(() => users.SaveChangesAsync());
    }

    [Fact]
    public async Task WorkspaceGraphPersistsWithMemberships()
    {
        await using var context = NewContext();
        var workspaces = new EfWorkspaceRepository(context);
        var ownerId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        await CreateUserAsync(context, ownerId, memberId);

        var workspace = Workspace.Create(WorkspaceName.Create("Acme Corp"), ownerId);
        workspace.AddMember(ownerId, MembershipRole.Owner, memberId, MembershipRole.Member);
        await workspaces.AddAsync(workspace);
        await workspaces.SaveChangesAsync();
        var workspaceId = workspace.Id;

        context.ChangeTracker.Clear();
        var loaded = await workspaces.FindByIdAsync(workspaceId);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Memberships.Count);
        Assert.Contains(loaded.Memberships, m => m.UserId == ownerId && m.Role == MembershipRole.Owner);
        Assert.Contains(loaded.Memberships, m => m.UserId == memberId && m.Role == MembershipRole.Member);
    }

    [Fact]
    public async Task DuplicateMembershipInSameWorkspaceIsRejectedBySchema()
    {
        await using var context = NewContext();
        var workspaces = new EfWorkspaceRepository(context);
        var ownerId = Guid.CreateVersion7();
        await CreateUserAsync(context, ownerId);

        var workspace = Workspace.Create(WorkspaceName.Create("Dupe Co"), ownerId);
        await workspaces.AddAsync(workspace);
        await workspaces.SaveChangesAsync();

        // Same (workspaceId, userId) pair inserted directly bypasses aggregate guards:
        // the schema-level unique index must still hold.
        await Assert.ThrowsAnyAsync<DbException>(() => context.Database.ExecuteSqlAsync($"""
            INSERT INTO identity.memberships ("Id", "WorkspaceId", "UserId", "Role")
            SELECT {Guid.CreateVersion7()}, m."WorkspaceId", m."UserId", 2
            FROM identity.memberships m
            WHERE m."WorkspaceId" = {workspace.Id} AND m."Role" = 1
            """));
    }

    [Fact]
    public async Task DeletingWorkspaceCascadesItsMemberships()
    {
        await using var context = NewContext();
        var workspaces = new EfWorkspaceRepository(context);
        var ownerId = Guid.CreateVersion7();
        await CreateUserAsync(context, ownerId);
        var workspace = Workspace.Create(WorkspaceName.Create("Cascade Co"), ownerId);
        await workspaces.AddAsync(workspace);
        await workspaces.SaveChangesAsync();
        var workspaceId = workspace.Id;

        context.ChangeTracker.Clear();
        var loaded = await context.Workspaces.Include(w => w.Memberships).SingleAsync(w => w.Id == workspaceId);
        context.Workspaces.Remove(loaded);
        await context.SaveChangesAsync();

        Assert.Empty(await context.Memberships.Where(m => m.WorkspaceId == workspaceId).ToListAsync());
    }
}

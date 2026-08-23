using Microsoft.EntityFrameworkCore;
using Qasedak.Modules.Identity.Application.Authentication;
using Qasedak.Modules.Identity.Application.Workspaces;
using Qasedak.Modules.Identity.Domain.Users;
using Qasedak.Modules.Identity.Domain.Workspaces;

namespace Qasedak.Modules.Identity.Infrastructure.Persistence;

/// <summary>Persistence-only credential record; the domain never carries password material.</summary>
public sealed class UserCredentials
{
    public UserCredentials(Guid userId, string passwordHash)
    {
        UserId = userId;
        PasswordHash = passwordHash;
    }

    public Guid UserId { get; private init; }

    public string PasswordHash { get; private init; }
}

/// <summary>EF Core repository over users and their credential material.</summary>
public sealed class EfUserRepository(IdentityDbContext context) : IUserRepository
{
    public Task<User?> FindByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default) =>
        context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<string?> GetPasswordHashAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.UserCredentials
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => c.PasswordHash)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task AddAsync(User user, string passwordHash, CancellationToken cancellationToken = default)
    {
        await context.Users.AddAsync(user, cancellationToken);
        await context.UserCredentials.AddAsync(new UserCredentials(user.Id, passwordHash), cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}

/// <summary>EF Core repository for workspace aggregates including their memberships.</summary>
public sealed class EfWorkspaceRepository(IdentityDbContext context) : IWorkspaceRepository
{
    /// <remarks>
    /// Returns the EF-materialized aggregate without re-running creation rules; the
    /// at-least-one-owner guarantee for stored state belongs to the write path and schema.
    /// </remarks>
    public Task<Workspace?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Workspaces
            .Include(w => w.Memberships)
            .AsNoTracking()
            .SingleOrDefaultAsync(w => w.Id == id, cancellationToken);

    /// <summary>Adds the aggregate graph; membership rows flow through the owned navigation.</summary>
    public async Task AddAsync(Workspace workspace, CancellationToken cancellationToken = default) =>
        await context.Workspaces.AddAsync(workspace, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}

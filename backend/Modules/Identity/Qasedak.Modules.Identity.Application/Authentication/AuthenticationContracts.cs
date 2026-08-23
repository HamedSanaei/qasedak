using Qasedak.Modules.Identity.Domain.Users;

namespace Qasedak.Modules.Identity.Application.Authentication;

/// <summary>Persistence contract for users and their credential material.</summary>
public interface IUserRepository
{
    /// <summary>Finds a user by canonical email address.</summary>
    Task<User?> FindByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default);

    /// <summary>Finds a user by identifier.</summary>
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns the stored password hash for a user, or null when absent.</summary>
    Task<string?> GetPasswordHashAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Registers a new user together with their credential hash.</summary>
    Task AddAsync(User user, string passwordHash, CancellationToken cancellationToken = default);

    /// <summary>Persists tracked changes as one atomic unit.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

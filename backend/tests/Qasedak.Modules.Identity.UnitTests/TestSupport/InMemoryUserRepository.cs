using Qasedak.Modules.Identity.Application.Authentication;
using Qasedak.Modules.Identity.Domain.Users;

namespace Qasedak.Modules.Identity.UnitTests.TestSupport;

/// <summary>
/// In-memory implementation of the user repository contract for use-case testing.
/// This is an interface fake for orchestration semantics; database behavior itself is
/// covered by real-PostgreSQL integration tests (M02-003).
/// </summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, (User User, string PasswordHash)> _byId = [];

    private readonly Dictionary<string, Guid> _idByEmail = new(StringComparer.Ordinal);

    public Task<User?> FindByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default) =>
        Task.FromResult(_idByEmail.TryGetValue(email.Value, out var id) ? _byId[id].User : null);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.TryGetValue(id, out var entry) ? entry.User : null);

    public Task<string?> GetPasswordHashAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byId.TryGetValue(userId, out var entry) ? entry.PasswordHash : null);

    public Task AddAsync(User user, string passwordHash, CancellationToken cancellationToken = default)
    {
        _byId[user.Id] = (user, passwordHash);
        _idByEmail[user.Email.Value] = user.Id;
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

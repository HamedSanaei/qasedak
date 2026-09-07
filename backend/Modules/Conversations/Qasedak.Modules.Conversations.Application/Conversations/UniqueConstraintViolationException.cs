namespace Qasedak.Modules.Conversations.Application.Conversations;

/// <summary>
/// Raised by persistence when PostgreSQL rejects a save with a unique-constraint
/// violation (SQLSTATE 23505). Lets application use cases converge on concurrent
/// inserts (thread creation races, duplicate provider ids) instead of surfacing a 500.
/// </summary>
public sealed class UniqueConstraintViolationException : Exception
{
    public UniqueConstraintViolationException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

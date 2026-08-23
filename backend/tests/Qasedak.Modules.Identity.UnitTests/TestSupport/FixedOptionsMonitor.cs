using Microsoft.Extensions.Options;

namespace Qasedak.Modules.Identity.UnitTests.TestSupport;

/// <summary>Static IOptionsMonitor stub for unit-testing option-sensitive adapters.</summary>
public sealed class FixedOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
{
    public T CurrentValue { get; } = currentValue;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

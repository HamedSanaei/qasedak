namespace Qasedak.BuildingBlocks.Domain;

/// <summary>
/// Qasedak-owned opaque identity of the exact connected channel account behind a
/// conversation, reply or automation (M13-002, ADR-011). Channel-neutral and
/// provider-neutral by construction: it carries no Instagram (or any provider)
/// type, no token and no host/IG identity — the Api composition root alone maps
/// it to one <c>ConnectedAccount.Id</c> at module boundaries. Conversations and
/// Automations persist and compare only this value; a missing value means a
/// legacy/unresolved pre-M13-002 record that must never route outbound traffic.
/// </summary>
public readonly record struct ChannelAccountId(Guid Value)
{
    /// <summary>Creates an identity for a real connected account; empty is rejected.</summary>
    public static ChannelAccountId From(Guid value) => value == Guid.Empty
        ? throw new ArgumentException("A channel account identity requires a non-empty account id.", nameof(value))
        : new ChannelAccountId(value);

    /// <summary>Parses the canonical Guid form; null/blank/invalid input yields null.</summary>
    public static ChannelAccountId? TryParse(string? value) =>
        Guid.TryParse(value, out var parsed) && parsed != Guid.Empty
            ? new ChannelAccountId(parsed)
            : null;

    /// <summary>Whether this instance names a real account (never true for default).</summary>
    public bool IsResolved => Value != Guid.Empty;

    public override string ToString() => Value.ToString("D");
}

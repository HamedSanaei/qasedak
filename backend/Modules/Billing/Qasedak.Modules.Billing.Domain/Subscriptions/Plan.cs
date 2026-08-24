namespace Qasedak.Modules.Billing.Domain;

/// <summary>
/// Server-owned entitlement limits granted by a plan. A limit of -1 means unlimited;
/// 0 means the feature is not part of the plan. Limits are plain integers — the
/// application layer decides what a "unit" is per feature key.
/// </summary>
public sealed record Entitlement(string FeatureKey, int Limit)
{
    public const int Unlimited = -1;

    public bool IsUnlimited => Limit == Unlimited;

    public bool IsEnabled => Limit != 0;

    public static Entitlement Of(string featureKey, int limit)
    {
        var normalized = featureKey?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new BillingDomainException("billing.featureKeyRequired", "An entitlement requires a feature key.");
        }

        if (limit < -1)
        {
            throw new BillingDomainException("billing.invalidLimit", "Entitlement limits are -1 (unlimited), 0 (disabled) or positive.");
        }

        return new Entitlement(normalized, limit);
    }
}

/// <summary>
/// A purchasable plan: stable code, display name and its entitlement grants. Plans are
/// immutable configuration owned by the server; subscriptions reference them by id.
/// </summary>
public sealed class Plan
{
    public const int MaxCodeLength = 40;
    public const int MaxNameLength = 100;
    public const int MaxFeaturesPerPlan = 32;

    private readonly List<Entitlement> _entitlements = [];

    private Plan()
    {
    }

    public Guid Id { get; private init; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public IReadOnlyList<Entitlement> Entitlements => _entitlements.AsReadOnly();

    public static Plan Create(Guid id, string code, string name, IEnumerable<Entitlement>? entitlements = null)
    {
        if (id == Guid.Empty)
        {
            throw new BillingDomainException("billing.invalidPlanId", "A plan requires an id.");
        }

        var normalizedCode = NormalizeCode(code);
        var planName = (name ?? string.Empty).Trim();
        if (planName.Length == 0)
        {
            throw new BillingDomainException("billing.planNameRequired", "A plan requires a name.");
        }

        if (planName.Length > MaxNameLength)
        {
            throw new BillingDomainException("billing.planNameTooLong", $"Plan names are limited to {MaxNameLength} characters.");
        }

        var plan = new Plan { Id = id, Code = normalizedCode, Name = planName };
        foreach (var entitlement in entitlements ?? [])
        {
            plan.AddEntitlement(entitlement);
        }

        return plan;
    }

    public void AddEntitlement(Entitlement entitlement)
    {
        if (_entitlements.Count >= MaxFeaturesPerPlan)
        {
            throw new BillingDomainException("billing.tooManyFeatures", $"Plans can grant at most {MaxFeaturesPerPlan} features.");
        }

        if (_entitlements.Any(e => e.FeatureKey == entitlement.FeatureKey))
        {
            // Latest definition wins: re-granting a feature replaces its limit.
            _entitlements.RemoveAll(e => e.FeatureKey == entitlement.FeatureKey);
        }

        _entitlements.Add(entitlement);
    }

    /// <summary>Effective entitlement for a feature; disabled when not granted. Case-insensitive.</summary>
    public Entitlement EntitlementFor(string featureKey)
    {
        var normalized = featureKey.Trim().ToLowerInvariant();
        return _entitlements.FirstOrDefault(e => string.Equals(e.FeatureKey, normalized, StringComparison.Ordinal))
            ?? Entitlement.Of(featureKey, 0);
    }

    private static string NormalizeCode(string? code)
    {
        var normalized = code?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new BillingDomainException("billing.planCodeRequired", "A plan requires a code.");
        }

        if (normalized.Length > MaxCodeLength)
        {
            throw new BillingDomainException("billing.planCodeTooLong", $"Plan codes are limited to {MaxCodeLength} characters.");
        }

        return normalized.ToLowerInvariant();
    }
}

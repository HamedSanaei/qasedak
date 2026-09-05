namespace Qasedak.Modules.Instagram.Infrastructure.Graph;

/// <summary>
/// Central Graph transport configuration, bound from "Instagram:Meta".
/// One configured host + version drives every versioned Graph path (M13-003);
/// OAuth's documented endpoints stay unversioned by contract and are configured
/// on MetaOAuthOptions, never here.
/// </summary>
public sealed class MetaGraphOptions
{
    public const string SectionName = "Instagram:Meta";

    /// <summary>Base host for versioned Graph calls (Instagram Login path).</summary>
    public string GraphHost { get; set; } = "https://graph.instagram.com";

    /// <summary>
    /// Graph API version segment (no slashes), e.g. "v26.0". Configured, not
    /// hardcoded: latest observed at M13-001 verification was v26.0.
    /// </summary>
    public string ApiVersion { get; set; } = "v26.0";

    /// <summary>Per-request timeout in seconds for Graph calls.</summary>
    public int TimeoutSeconds { get; set; } = 100;
}

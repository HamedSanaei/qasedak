namespace Qasedak.Modules.Instagram.Infrastructure.Graph;

/// <summary>
/// Central URI construction for Meta Graph calls (M13-003). Versioned Graph paths
/// take {host}/{version}/{path}; endpoints whose official contract is unversioned
/// (OAuth authorize, api.instagram.com token exchange) never pass through here.
/// </summary>
public static class MetaGraphUris
{
    /// <summary>Builds a versioned Graph URI; the version segment is normalized once here.</summary>
    public static Uri Versioned(string host, string version, string path, string? query = null)
    {
        var normalizedVersion = version.Trim().Trim('/');
        var normalizedPath = path.Trim().Trim('/');
        var builder = new UriBuilder(new Uri(new Uri(EnsureTrailingSlash(host)), $"{normalizedVersion}/{normalizedPath}"));
        if (!string.IsNullOrEmpty(query))
        {
            builder.Query = query;
        }

        return builder.Uri;
    }

    private static string EnsureTrailingSlash(string host) =>
        host.EndsWith('/') ? host : host + "/";
}

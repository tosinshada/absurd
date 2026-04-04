using Microsoft.AspNetCore.Http;

namespace Absurd.Dashboard.Internal;

/// <summary>
/// Path utilities ported from the Go habitat implementation.
/// Handles forwarded-prefix extraction, path normalization, and runtime config computation.
/// </summary>
internal static class PathHelpers
{
    private static readonly string[] ForwardedPrefixHeaders =
        ["X-Forwarded-Prefix", "X-Forwarded-Path", "X-Script-Name"];

    /// <summary>
    /// Extracts the effective forwarded path prefix from the request headers.
    /// Returns the last non-empty comma-separated segment from the first matching header.
    /// Port of Go's <c>extractForwardedPrefix</c>.
    /// </summary>
    internal static string ExtractForwardedPrefix(HttpRequest request)
    {
        foreach (var headerName in ForwardedPrefixHeaders)
        {
            var raw = request.Headers[headerName].ToString().Trim();
            if (string.IsNullOrEmpty(raw))
                continue;

            var parts = raw.Split(',');
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                var value = parts[i].Trim();
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Normalizes a path prefix: ensures it starts with '/', strips trailing '/',
    /// strips query/fragment characters, and returns "" for empty or root paths.
    /// Port of Go's <c>normalizePathPrefix</c>.
    /// </summary>
    internal static string NormalizePathPrefix(string value)
    {
        value = value.Trim();
        if (string.IsNullOrEmpty(value) || value == "/")
            return string.Empty;

        // Strip query string or fragment
        var idx = value.IndexOfAny(['?', '#']);
        if (idx >= 0)
            value = value[..idx];

        // Ensure leading slash, no double-slash at start, no trailing slash
        if (!value.StartsWith('/'))
            value = "/" + value;

        value = "/" + value.TrimStart('/');
        value = value.TrimEnd('/');

        return value == "/" ? string.Empty : value;
    }

    /// <summary>
    /// Joins path prefix segments, normalizing and concatenating non-empty parts.
    /// Port of Go's <c>joinPathPrefixes</c>.
    /// </summary>
    internal static string JoinPathPrefixes(params string[] parts)
    {
        var result = string.Empty;
        foreach (var part in parts)
        {
            var normalized = NormalizePathPrefix(part);
            if (string.IsNullOrEmpty(normalized))
                continue;

            result += normalized;
        }

        return result;
    }

    /// <summary>
    /// Resolves the effective public base path for the current request by merging
    /// the configured mount path with any forwarded prefix from proxy headers.
    /// Port of Go's <c>publicBasePath</c>.
    /// </summary>
    internal static string ResolvePublicBasePath(HttpRequest request, string configuredBasePath)
    {
        var forwardedPrefix = NormalizePathPrefix(ExtractForwardedPrefix(request));

        if (string.IsNullOrEmpty(forwardedPrefix))
            return configuredBasePath;

        if (string.IsNullOrEmpty(configuredBasePath))
            return forwardedPrefix;

        // If the forwarded prefix already ends with the configured path, use it as-is
        if (forwardedPrefix.EndsWith(configuredBasePath, StringComparison.OrdinalIgnoreCase))
            return forwardedPrefix;

        return JoinPathPrefixes(forwardedPrefix, configuredBasePath);
    }

    /// <summary>
    /// Builds the full runtime configuration object used by the SPA.
    /// </summary>
    internal static RuntimeConfig BuildRuntimeConfig(HttpRequest request, string configuredBasePath)
    {
        var basePath = ResolvePublicBasePath(request, configuredBasePath);
        return new RuntimeConfig(
            BasePath: basePath,
            ApiBasePath: JoinPathPrefixes(basePath, "/api"),
            StaticBasePath: JoinPathPrefixes(basePath, "/_static"));
    }
}

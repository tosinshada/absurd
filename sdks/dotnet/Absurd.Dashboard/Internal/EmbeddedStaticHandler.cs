using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace Absurd.Dashboard.Internal;

/// <summary>
/// Serves static assets embedded in the assembly from the <c>/_static/**</c> sub-path.
/// Assets are embedded at build time from the <c>wwwroot/</c> directory.
/// </summary>
internal static class EmbeddedStaticHandler
{
    private static readonly Assembly Assembly = typeof(EmbeddedStaticHandler).Assembly;

    // Cache resource names so we don't re-query the assembly on every request.
    private static readonly Lazy<HashSet<string>> ResourceNames = new(
        () => new HashSet<string>(Assembly.GetManifestResourceNames(), StringComparer.OrdinalIgnoreCase),
        isThreadSafe: true);

    // Covers .js, .css, .woff2, .woff, .ttf, .png, .svg, .json, .ico, .map, etc.
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    /// <summary>
    /// The embedded resource name prefix corresponding to the <c>wwwroot/</c> folder.
    /// Namespace + folder path with '/' replaced by '.'.
    /// </summary>
    private const string ResourcePrefix = "Absurd.Dashboard.wwwroot.";

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Attempts to serve a static asset embedded in the assembly.
    /// <paramref name="requestPath"/> is the path relative to the <c>/_static/</c> prefix
    /// (e.g. <c>/assets/index-abc123.js</c>).
    /// Returns <c>true</c> when the response was written; <c>false</c> when the file
    /// was not found (caller should return HTTP 404).
    /// </summary>
    public static async Task<bool> TryServeAsync(HttpContext context, string requestPath)
    {
        var resourceName = ResolveResourceName(requestPath);
        if (resourceName is null)
            return false;

        using var stream = Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return false;

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = ResolveContentType(requestPath);
        context.Response.ContentLength = stream.Length;
        await stream.CopyToAsync(context.Response.Body);
        return true;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Maps a URL sub-path to an embedded resource name.
    /// E.g. <c>/assets/index-abc123.js</c> → <c>Absurd.Dashboard.wwwroot.assets.index-abc123.js</c>
    /// </summary>
    private static string? ResolveResourceName(string requestPath)
    {
        // Normalise: strip leading slash, replace path separators with dots
        var normalized = requestPath
            .TrimStart('/', '\\')
            .Replace('/', '.')
            .Replace('\\', '.');

        if (string.IsNullOrEmpty(normalized))
            return null;

        var candidate = ResourcePrefix + normalized;

        return ResourceNames.Value.Contains(candidate) ? candidate : null;
    }

    /// <summary>
    /// Resolves the MIME type for the given file path using the ASP.NET Core
    /// <see cref="FileExtensionContentTypeProvider"/>, falling back to
    /// <c>application/octet-stream</c>.
    /// </summary>
    private static string ResolveContentType(string requestPath)
    {
        if (ContentTypeProvider.TryGetContentType(requestPath, out var contentType))
            return contentType;

        return "application/octet-stream";
    }
}

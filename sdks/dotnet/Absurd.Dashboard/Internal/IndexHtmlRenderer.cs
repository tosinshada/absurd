using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Absurd.Dashboard.Internal;

/// <summary>
/// Loads the embedded <c>index.html</c>, injects the runtime config, and serves the SPA.
/// Port of Go's <c>renderIndexHTML</c> / <c>handleIndex</c>.
/// </summary>
internal sealed class IndexHtmlRenderer
{
    private static readonly Assembly Assembly = typeof(IndexHtmlRenderer).Assembly;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly byte[]? _indexHtml;

    public bool IsAvailable => _indexHtml is not null;

    public IndexHtmlRenderer()
    {
        _indexHtml = LoadIndexHtml();
    }

    // ---------------------------------------------------------------------------
    // Loading
    // ---------------------------------------------------------------------------

    private static byte[]? LoadIndexHtml()
    {
        var resourceName = FindResourceName("index.html");
        if (resourceName is null)
            return null;

        using var stream = Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
            return null;

        using var ms = new MemoryStream((int)stream.Length);
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static string? FindResourceName(string fileName)
    {
        foreach (var name in Assembly.GetManifestResourceNames())
        {
            if (name.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        return null;
    }

    // ---------------------------------------------------------------------------
    // Rendering
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Renders the index.html bytes with the runtime config injected.
    /// Port of Go's <c>renderIndexHTML</c>.
    /// </summary>
    public byte[] Render(RuntimeConfig config)
    {
        if (_indexHtml is null)
            return [];

        var baseHref = string.IsNullOrEmpty(config.BasePath) ? "/" : config.BasePath + "/";

        var payload = JsonSerializer.Serialize(config, JsonOptions);
        var encodedHref = System.Net.WebUtility.HtmlEncode(baseHref);
        var injection = $"<base href=\"{encodedHref}\"><script>window.__HABITAT_RUNTIME_CONFIG__={payload};</script>";

        var document = Encoding.UTF8.GetString(_indexHtml);

        // Rewrite /_static/ references to the effective static base path
        var staticPrefix = config.StaticBasePath + "/";
        document = document.Replace("\"/_static/", $"\"{staticPrefix}", StringComparison.Ordinal);
        document = document.Replace("'/_static/", $"'{staticPrefix}", StringComparison.Ordinal);

        // Inject before </head> (or prepend if no </head>)
        var headEndIdx = document.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        document = headEndIdx >= 0
            ? document[..headEndIdx] + injection + document[headEndIdx..]
            : injection + document;

        return Encoding.UTF8.GetBytes(document);
    }

    // ---------------------------------------------------------------------------
    // Serving
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Writes the rendered index.html to the response.
    /// Returns false and writes a 503 when the frontend assets have not been built.
    /// Port of Go's <c>handleIndex</c>.
    /// </summary>
    public async Task<bool> TrySendAsync(HttpContext context, RuntimeConfig config)
    {
        if (!IsAvailable)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(
                "Frontend assets not built. Run 'make dashboard-ui' to build the SolidJS bundle.");
            return false;
        }

        var html = Render(config);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength = html.Length;
        await context.Response.Body.WriteAsync(html);
        return true;
    }

    // ---------------------------------------------------------------------------
    // SPA fallback decision (4.5)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns true when the request path should be handled as a SPA deep-link
    /// (i.e., not an API route and not an internal /_* route).
    /// </summary>
    public static bool IsSpaRequest(string path) =>
        !path.StartsWith("/api", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith("/_", StringComparison.OrdinalIgnoreCase);
}

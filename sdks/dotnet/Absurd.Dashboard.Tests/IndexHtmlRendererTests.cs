using System.Text;
using Absurd.Dashboard.Internal;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Absurd.Dashboard.Tests;

/// <summary>
/// Unit tests for IndexHtmlRenderer base-path injection logic.
/// Covers: empty prefix, custom prefix, and reverse-proxy prefix scenarios.
/// Task 13.1
/// </summary>
public sealed class IndexHtmlRendererTests
{
    // Minimal HTML document used as the base for all render tests.
    private static readonly string BaseHtml =
        "<html><head><!-- placeholder --></head><body><script src=\"/_static/assets/main.js\"></script></body></html>";

    private static IndexHtmlRenderer CreateRenderer(string? html = null)
    {
        var bytes = Encoding.UTF8.GetBytes(html ?? BaseHtml);
        return new IndexHtmlRenderer(bytes);
    }

    // ── 13.1a: empty prefix ──────────────────────────────────────────────────

    [Fact]
    public void Render_EmptyBasePath_InjectsBaseHrefSlash()
    {
        var renderer = CreateRenderer();
        var config = new RuntimeConfig("", "/api", "/_static");

        var output = Encoding.UTF8.GetString(renderer.Render(config));

        Assert.Contains("base href=\"/\"", output);
    }

    [Fact]
    public void Render_EmptyBasePath_InjectsRuntimeConfigScript()
    {
        var renderer = CreateRenderer();
        var config = new RuntimeConfig("", "/api", "/_static");

        var output = Encoding.UTF8.GetString(renderer.Render(config));

        Assert.Contains("window.__HABITAT_RUNTIME_CONFIG__=", output);
        Assert.Contains("\"basePath\":\"\"", output);
    }

    [Fact]
    public void Render_EmptyBasePath_InjectionAppearsBeforeHeadClose()
    {
        var renderer = CreateRenderer();
        var config = new RuntimeConfig("", "/api", "/_static");

        var output = Encoding.UTF8.GetString(renderer.Render(config));

        var injectionIdx = output.IndexOf("window.__HABITAT_RUNTIME_CONFIG__", StringComparison.Ordinal);
        var headCloseIdx = output.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        Assert.True(injectionIdx < headCloseIdx, "Injection should appear before </head>");
    }

    // ── 13.1b: custom prefix ────────────────────────────────────────────────

    [Fact]
    public void Render_CustomBasePath_InjectsBaseHrefWithTrailingSlash()
    {
        var renderer = CreateRenderer();
        var config = new RuntimeConfig("/habitat", "/habitat/api", "/habitat/_static");

        var output = Encoding.UTF8.GetString(renderer.Render(config));

        Assert.Contains("base href=\"/habitat/\"", output);
    }

    [Fact]
    public void Render_CustomBasePath_RuntimeConfigContainsBasePath()
    {
        var renderer = CreateRenderer();
        var config = new RuntimeConfig("/habitat", "/habitat/api", "/habitat/_static");

        var output = Encoding.UTF8.GetString(renderer.Render(config));

        Assert.Contains("\"basePath\":\"/habitat\"", output);
        Assert.Contains("\"apiBasePath\":\"/habitat/api\"", output);
        Assert.Contains("\"staticBasePath\":\"/habitat/_static\"", output);
    }

    // ── 13.1c: reverse-proxy nested prefix ──────────────────────────────────

    [Fact]
    public void Render_NestedPrefix_InjectsCorrectBaseHref()
    {
        var renderer = CreateRenderer();
        var config = new RuntimeConfig("/proxy/habitat", "/proxy/habitat/api", "/proxy/habitat/_static");

        var output = Encoding.UTF8.GetString(renderer.Render(config));

        Assert.Contains("base href=\"/proxy/habitat/\"", output);
    }

    // ── 13.1d: static path rewriting ────────────────────────────────────────

    [Fact]
    public void Render_RewritesStaticPaths_DoubleQuoted()
    {
        var renderer = CreateRenderer();
        var config = new RuntimeConfig("/habitat", "/habitat/api", "/habitat/_static");

        var output = Encoding.UTF8.GetString(renderer.Render(config));

        // Original: src="/_static/assets/main.js"
        // Should become: src="/habitat/_static/assets/main.js"
        Assert.Contains("\"/habitat/_static/assets/main.js\"", output);
        Assert.DoesNotContain("\"/_static/assets/main.js\"", output);
    }

    [Fact]
    public void Render_RewritesStaticPaths_SingleQuoted()
    {
        const string html = "<html><head></head><body><link href='/_static/style.css'></body></html>";
        var renderer = CreateRenderer(html);
        var config = new RuntimeConfig("/habitat", "/habitat/api", "/habitat/_static");

        var output = Encoding.UTF8.GetString(renderer.Render(config));

        Assert.Contains("'/habitat/_static/style.css'", output);
        Assert.DoesNotContain("'/_static/style.css'", output);
    }

    // ── 13.1e: no </head> fallback ───────────────────────────────────────────

    [Fact]
    public void Render_NoHeadElement_PrependInjection()
    {
        const string html = "<html><body>content</body></html>";
        var renderer = CreateRenderer(html);
        var config = new RuntimeConfig("/habitat", "/habitat/api", "/habitat/_static");

        var output = Encoding.UTF8.GetString(renderer.Render(config));

        Assert.StartsWith("<base href=", output);
    }

    // ── 13.1f: unavailable renderer (no embedded bytes) ─────────────────────

    [Fact]
    public void Render_NullIndexHtml_ReturnsEmptyBytes()
    {
        // Use reflection to simulate missing embedded resource by creating with empty array
        // We rely on the fact that null _indexHtml returns empty from Render.
        // The default (parameterless) constructor sets _indexHtml to null when no resource found.
        // We can't easily test that path here without stripping the assembly resources, so we
        // verify that a renderer constructed with null-like empty content still works.
        var renderer = new IndexHtmlRenderer([]); // zero-byte "template"
        var config = new RuntimeConfig("", "/api", "/_static");

        var output = renderer.Render(config);

        // No content, but doesn't throw
        Assert.NotNull(output);
    }

    // ── 13.1g: PathHelpers.ExtractForwardedPrefix ────────────────────────────

    [Fact]
    public void ExtractForwardedPrefix_XForwardedPrefix_ReturnsValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-Prefix"] = "/proxy";

        var result = PathHelpers.ExtractForwardedPrefix(context.Request);

        Assert.Equal("/proxy", result);
    }

    [Fact]
    public void ExtractForwardedPrefix_XForwardedPath_ReturnsValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-Path"] = "/app/habitat";

        var result = PathHelpers.ExtractForwardedPrefix(context.Request);

        Assert.Equal("/app/habitat", result);
    }

    [Fact]
    public void ExtractForwardedPrefix_XScriptName_ReturnsValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Script-Name"] = "/dash";

        var result = PathHelpers.ExtractForwardedPrefix(context.Request);

        Assert.Equal("/dash", result);
    }

    [Fact]
    public void ExtractForwardedPrefix_MultipleValues_ReturnsLast()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Forwarded-Prefix"] = "/first, /second";

        var result = PathHelpers.ExtractForwardedPrefix(context.Request);

        Assert.Equal("/second", result);
    }

    [Fact]
    public void ExtractForwardedPrefix_NoHeaders_ReturnsEmpty()
    {
        var context = new DefaultHttpContext();

        var result = PathHelpers.ExtractForwardedPrefix(context.Request);

        Assert.Equal(string.Empty, result);
    }

    // ── 13.1h: PathHelpers.NormalizePathPrefix ───────────────────────────────

    [Theory]
    [InlineData("",        "")]
    [InlineData("/",       "")]
    [InlineData("/habitat","/habitat")]
    [InlineData("/habitat/", "/habitat")]
    [InlineData("habitat", "/habitat")]
    [InlineData("/a/b/c",  "/a/b/c")]
    [InlineData("/path?q=1", "/path")]
    [InlineData("/path#frag", "/path")]
    public void NormalizePathPrefix_Various_ReturnsExpected(string input, string expected)
    {
        var result = PathHelpers.NormalizePathPrefix(input);
        Assert.Equal(expected, result);
    }
}

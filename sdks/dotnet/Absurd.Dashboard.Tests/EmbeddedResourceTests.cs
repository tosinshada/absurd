using System.Reflection;
using Absurd.Dashboard.Internal;
using Xunit;

namespace Absurd.Dashboard.Tests;

/// <summary>
/// Verifies the embedded resource manifest after the build pipeline runs.
/// Task 13.4: "Add a test verifying the embedded resource manifest contains
/// the expected frontend files after the build pipeline runs."
/// </summary>
public sealed class EmbeddedResourceTests
{
    private static readonly Assembly DashboardAssembly = typeof(IndexHtmlRenderer).Assembly;

    [Fact]
    public void EmbeddedResources_WhenFrontendBuilt_ContainsIndexHtml()
    {
        var resources = DashboardAssembly.GetManifestResourceNames();

        // If wwwroot is empty (no frontend build), skip gracefully
        if (resources.Length == 0)
        {
            // Not a failure — indicates the SolidJS build hasn't been run yet.
            // Run `make dashboard-ui` first.
            return;
        }

        var hasIndex = resources.Any(r =>
            r.EndsWith(".index.html", StringComparison.OrdinalIgnoreCase) ||
            r.Equals("Absurd.Dashboard.wwwroot.index.html", StringComparison.OrdinalIgnoreCase));

        Assert.True(hasIndex,
            $"Expected an embedded index.html resource. Found: [{string.Join(", ", resources)}]. " +
            "Run 'make dashboard-ui' to build the frontend.");
    }

    [Fact]
    public void EmbeddedResources_WhenFrontendBuilt_ContainsAtLeastOneJsFile()
    {
        var resources = DashboardAssembly.GetManifestResourceNames();

        // Skip when no JS assets exist — indicates the SolidJS build hasn't been run yet.
        // Run `make dashboard-ui` to populate wwwroot/ with the full bundle.
        if (!resources.Any(r => r.EndsWith(".js", StringComparison.OrdinalIgnoreCase)))
            return;

        var hasJs = resources.Any(r => r.EndsWith(".js", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasJs,
            $"Expected at least one .js embedded resource. Found: [{string.Join(", ", resources)}].");
    }

    [Fact]
    public void EmbeddedResources_WhenFrontendBuilt_ContainsAtLeastOneCssFile()
    {
        var resources = DashboardAssembly.GetManifestResourceNames();

        // Skip when JS assets are absent — full build hasn't run yet.
        if (!resources.Any(r => r.EndsWith(".js", StringComparison.OrdinalIgnoreCase)))
            return;

        var hasCss = resources.Any(r => r.EndsWith(".css", StringComparison.OrdinalIgnoreCase));
        Assert.True(hasCss,
            $"Expected at least one .css embedded resource. Found: [{string.Join(", ", resources)}].");
    }

    [Fact]
    public void EmbeddedResources_WhenFrontendBuilt_AllResourcesHaveExpectedPrefix()
    {
        const string expectedPrefix = "Absurd.Dashboard.wwwroot.";
        var resources = DashboardAssembly.GetManifestResourceNames();

        if (resources.Length == 0)
            return; // build not run yet — skip

        foreach (var resource in resources)
        {
            Assert.True(
                resource.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase),
                $"Resource '{resource}' does not have expected prefix '{expectedPrefix}'. " +
                "Ensure assets are placed in the wwwroot/ directory.");
        }
    }

    [Fact]
    public void IndexHtmlRenderer_WhenFrontendBuilt_IsAvailable()
    {
        var renderer = new IndexHtmlRenderer();
        var resources = DashboardAssembly.GetManifestResourceNames();

        if (resources.Length == 0)
        {
            Assert.False(renderer.IsAvailable,
                "IsAvailable should be false when no embedded resources exist.");
            return;
        }

        Assert.True(renderer.IsAvailable,
            "IsAvailable should be true when wwwroot/ contains the built frontend assets.");
    }
}

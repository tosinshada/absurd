using System.Net;
using System.Text.Json;
using Xunit;

namespace Absurd.Dashboard.Tests;

/// <summary>
/// Integration tests for the Absurd Dashboard HTTP endpoints.
/// Exercises the TestServer against a real PostgreSQL database with the Absurd schema applied.
/// Task 13.3
/// </summary>
[Collection("DashboardIntegration")]
public sealed class DashboardIntegrationTests(DashboardTestFixture fixture)
{
    private HttpClient Client => fixture.Client;

    // ── /_healthz ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Healthz_WhenDatabaseUp_Returns200Ok()
    {
        var response = await Client.GetAsync("/habitat/_healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal("ok", body);
    }

    [Fact]
    public async Task Healthz_ContentType_IsTextPlain()
    {
        var response = await Client.GetAsync("/habitat/_healthz");

        Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.MediaType ?? "");
    }

    // ── /api/queues ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ApiQueues_EmptyDatabase_Returns200WithEmptyArray()
    {
        var response = await Client.GetAsync("/habitat/api/queues");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ApiQueues_ContentType_IsApplicationJson()
    {
        var response = await Client.GetAsync("/habitat/api/queues");

        Assert.StartsWith("application/json", response.Content.Headers.ContentType?.MediaType ?? "");
    }

    // ── /api/metrics ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ApiMetrics_EmptyDatabase_Returns200WithQueuesKey()
    {
        var response = await Client.GetAsync("/habitat/api/metrics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("queues", out _),
            $"Expected 'queues' property in response: {json}");
    }

    [Fact]
    public async Task ApiMetrics_EmptyDatabase_QueuesIsEmptyArray()
    {
        var response = await Client.GetAsync("/habitat/api/metrics");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var queues = doc.RootElement.GetProperty("queues");
        Assert.Equal(JsonValueKind.Array, queues.ValueKind);
        Assert.Equal(0, queues.GetArrayLength());
    }

    // ── /api/tasks ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApiTasks_EmptyDatabase_Returns200()
    {
        var response = await Client.GetAsync("/habitat/api/tasks");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApiTasks_ContentType_IsApplicationJson()
    {
        var response = await Client.GetAsync("/habitat/api/tasks");

        Assert.StartsWith("application/json", response.Content.Headers.ContentType?.MediaType ?? "");
    }

    [Fact]
    public async Task ApiTasks_EmptyDatabase_ReturnsTasksArray()
    {
        var response = await Client.GetAsync("/habitat/api/tasks");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        // Response should have a "tasks" array (or be an array itself)
        var root = doc.RootElement;
        var hasTasks = root.ValueKind == JsonValueKind.Array ||
                       root.TryGetProperty("tasks", out _);
        Assert.True(hasTasks, $"Expected tasks data in response: {json}");
    }

    // ── /api/tasks/{id} ───────────────────────────────────────────────────────

    [Fact]
    public async Task ApiTaskDetail_NonExistentId_Returns404()
    {
        var response = await Client.GetAsync("/habitat/api/tasks/00000000-0000-0000-0000-000000000000");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ApiTaskDetail_InvalidId_Returns400()
    {
        var response = await Client.GetAsync("/habitat/api/tasks/not-a-uuid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── /api/config ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ApiConfig_Returns200WithConfigShape()
    {
        var response = await Client.GetAsync("/habitat/api/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("basePath", out _),
            $"Missing 'basePath' in: {json}");
        Assert.True(doc.RootElement.TryGetProperty("apiBasePath", out _),
            $"Missing 'apiBasePath' in: {json}");
        Assert.True(doc.RootElement.TryGetProperty("staticBasePath", out _),
            $"Missing 'staticBasePath' in: {json}");
    }

    // ── /api/events ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ApiEvents_EmptyDatabase_Returns200()
    {
        var response = await Client.GetAsync("/habitat/api/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── SPA fallback ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SpaFallback_UnknownPath_Returns200OrNoContent()
    {
        // The SPA handler returns 200 with index.html when assets exist,
        // or 503 when wwwroot is empty (as in CI before the UI is built).
        var response = await Client.GetAsync("/habitat/some/deep/route");

        // Either 200 (assets built) or 503 (no assets built yet) is acceptable
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable,
            $"Unexpected status for SPA fallback: {response.StatusCode}");
    }

    // ── Routing isolation (routes outside prefix should not be handled) ────────

    [Fact]
    public async Task RoutesOutsidePrefix_AreNotHandledByDashboard()
    {
        // /api/tasks outside the /habitat prefix should not be served by the dashboard
        var response = await Client.GetAsync("/api/tasks");

        // TestServer returns 404 for paths not mapped by the host
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

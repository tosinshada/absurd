using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Absurd.Dashboard.Internal;

/// <summary>
/// Shared HTTP response helpers — port of Go's <c>writeJSON</c> and error helpers.
/// </summary>
internal static class ResponseHelpers
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    /// <summary>
    /// Serializes <paramref name="payload"/> as JSON and writes it to the response.
    /// Sets <c>Content-Type: application/json</c> and the given <paramref name="statusCode"/>.
    /// Port of Go's <c>writeJSON</c>.
    /// </summary>
    internal static async Task WriteJsonAsync(HttpResponse response, int statusCode, object? payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        response.ContentLength = bytes.Length;
        await response.Body.WriteAsync(bytes);
    }

    /// <summary>
    /// Writes an <c>{ "error": "..." }</c> JSON error response.
    /// </summary>
    internal static Task WriteErrorAsync(HttpResponse response, int statusCode, string message) =>
        WriteJsonAsync(response, statusCode, new ErrorResponse(message));

    private sealed record ErrorResponse([property: System.Text.Json.Serialization.JsonPropertyName("error")] string Error);
}

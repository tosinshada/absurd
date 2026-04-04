using System.Text.Json.Serialization;

namespace Absurd.Dashboard.Internal;

/// <summary>
/// Runtime configuration injected into index.html as <c>window.__HABITAT_RUNTIME_CONFIG__</c>.
/// Matches the JSON shape expected by the SolidJS frontend.
/// </summary>
internal sealed record RuntimeConfig(
    [property: JsonPropertyName("basePath")] string BasePath,
    [property: JsonPropertyName("apiBasePath")] string ApiBasePath,
    [property: JsonPropertyName("staticBasePath")] string StaticBasePath);

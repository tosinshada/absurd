using System.Text.Json;

namespace Absurd.Internal;

/// <summary>
/// Internal record of a registered task. Holds the resolved queue name,
/// defaults, and the untyped handler delegate.
/// </summary>
internal sealed class RegisteredTask
{
    public required string Name { get; init; }
    public required string Queue { get; init; }
    public int? DefaultMaxAttempts { get; init; }
    public CancellationPolicy? DefaultCancellation { get; init; }

    /// <summary>
    /// Untyped handler that accepts the raw JSON params element and a TaskContext.
    /// Deserialization to the concrete TParams is captured in the closure by
    /// <see cref="AbsurdClient.RegisterTask{TParams}"/>.
    /// </summary>
    public required Func<JsonElement, TaskContext, Task> Handler { get; init; }
}

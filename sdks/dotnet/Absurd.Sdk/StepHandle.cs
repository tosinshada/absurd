using System.Text.Json;

namespace Absurd;

/// <summary>
/// Handle returned by <see cref="TaskContext.BeginStepAsync{T}"/>.
/// Check <see cref="IsDone"/> before calling <see cref="TaskContext.CompleteStepAsync{T}"/>.
/// </summary>
public sealed class StepHandle<T>
{
    /// <summary>The logical step name provided by the caller.</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The concrete checkpoint key used in Postgres (includes automatic
    /// numbering for repeated step names, e.g. <c>fetch#2</c>).
    /// </summary>
    public required string CheckpointName { get; init; }

    /// <summary><c>true</c> if the checkpoint already exists and the cached value is available.</summary>
    public required bool IsDone { get; init; }

    /// <summary>
    /// The cached checkpoint value. Only valid when <see cref="IsDone"/> is <c>true</c>.
    /// </summary>
    public T? State { get; init; }
}

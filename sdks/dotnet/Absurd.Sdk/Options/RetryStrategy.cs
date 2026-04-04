namespace Absurd.Options;

/// <summary>
/// Base class for all retry strategies.
/// Use one of the concrete subtypes: <see cref="FixedRetryStrategy"/>,
/// <see cref="ExponentialRetryStrategy"/>, or <see cref="NoRetryStrategy"/>.
/// </summary>
public abstract class RetryStrategy { }

/// <summary>
/// Waits a fixed number of seconds between each retry attempt.
/// </summary>
public sealed class FixedRetryStrategy : RetryStrategy
{
    /// <summary>Seconds to wait between retries.</summary>
    public required double BaseSeconds { get; init; }
}

/// <summary>
/// Waits <c>BaseSeconds * Factor^attempt</c> between retries, capped at <see cref="MaxSeconds"/>.
/// </summary>
public sealed class ExponentialRetryStrategy : RetryStrategy
{
    /// <summary>Initial wait in seconds.</summary>
    public required double BaseSeconds { get; init; }

    /// <summary>Multiplier applied each attempt.</summary>
    public double Factor { get; init; } = 2.0;

    /// <summary>Maximum wait ceiling in seconds.</summary>
    public double? MaxSeconds { get; init; }
}

/// <summary>
/// Disables automatic retries entirely.
/// </summary>
public sealed class NoRetryStrategy : RetryStrategy { }

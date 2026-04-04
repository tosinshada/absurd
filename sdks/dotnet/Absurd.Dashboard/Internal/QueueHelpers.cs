using System.Text.Json;

namespace Absurd.Dashboard.Internal;

/// <summary>
/// SQL quoting utilities and query helpers ported from the Go habitat implementation.
/// </summary>
internal static class QueueHelpers
{
    // ---------------------------------------------------------------------------
    // SQL Identifier / Literal quoting (port of pq.QuoteIdentifier / pq.QuoteLiteral)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Quotes a PostgreSQL identifier with double quotes, escaping any embedded double quotes.
    /// Port of Go's <c>pq.QuoteIdentifier</c>.
    /// </summary>
    internal static string QuoteIdentifier(string identifier) =>
        "\"" + identifier.Replace("\"", "\"\"") + "\"";

    /// <summary>
    /// Quotes a string as a PostgreSQL literal (single-quoted), escaping backslashes and quotes.
    /// Port of Go's <c>pq.QuoteLiteral</c>.
    /// NOTE: Only use for values that have been validated against the database (e.g., queue names
    /// fetched from <c>absurd.queues</c>). Never use with arbitrary user input.
    /// </summary>
    internal static string QuoteLiteral(string value) =>
        "'" + value.Replace("\\", "\\\\").Replace("'", "''") + "'";

    /// <summary>
    /// Builds the qualified table identifier for an Absurd per-queue table.
    /// E.g., prefix="t", queueName="orders" → <c>"t_orders"</c> (double-quoted).
    /// Port of Go's <c>queueTableIdentifier</c>.
    /// </summary>
    internal static string QueueTableIdentifier(string prefix, string queueName) =>
        QuoteIdentifier(prefix + "_" + queueName);

    // ---------------------------------------------------------------------------
    // Parsing helpers (port of Go parsePositiveInt / parseOptionalTime)
    // ---------------------------------------------------------------------------

    internal static int ParsePositiveInt(string? value, int fallback)
    {
        if (string.IsNullOrEmpty(value))
            return fallback;

        if (int.TryParse(value, out var parsed) && parsed > 0)
            return parsed;

        return fallback;
    }

    internal static DateTime? ParseOptionalTime(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        if (DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t))
            return t;

        return null;
    }

    // ---------------------------------------------------------------------------
    // Task status helpers
    // ---------------------------------------------------------------------------

    private static readonly string[] KnownStatuses =
        ["pending", "running", "sleeping", "completed", "failed", "cancelled"];

    internal static string[] AllTaskStatuses() => KnownStatuses;

    /// <summary>
    /// Normalises and validates a status filter string.
    /// Returns (status, true) when valid or empty; (null, false) when unrecognised.
    /// </summary>
    internal static (string? status, bool valid) NormalizeTaskStatusFilter(string? value)
    {
        var status = value?.Trim().ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(status))
            return ("", true);

        foreach (var candidate in KnownStatuses)
        {
            if (status == candidate)
                return (status, true);
        }

        return (null, false);
    }

    // ---------------------------------------------------------------------------
    // JSON element helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Parses a nullable JSON string from the database into a <see cref="JsonElement"/>.
    /// Returns null when the value is null/empty.
    /// </summary>
    internal static JsonElement? ParseJsonElement(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------------------
    // Task search filter (port of Go matchesTaskFilters)
    // ---------------------------------------------------------------------------

    internal static bool MatchesTaskSearch(
        string taskId, string runId, string queueName, string taskName, string? paramsJson,
        string search)
    {
        if (string.IsNullOrEmpty(search))
            return true;

        var s = search.ToLowerInvariant();
        return taskId.ToLowerInvariant().Contains(s)
            || runId.ToLowerInvariant().Contains(s)
            || queueName.ToLowerInvariant().Contains(s)
            || taskName.ToLowerInvariant().Contains(s)
            || (paramsJson != null && paramsJson.ToLowerInvariant().Contains(s));
    }
}

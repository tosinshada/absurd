using System.Collections.Concurrent;
using Npgsql;

namespace Absurd.Dashboard.Internal;

/// <summary>
/// 1-minute in-memory cache for recent task names per queue.
/// Port of Go's <c>recentTaskNamesCache</c> + <c>getRecentQueueTaskNamesCached</c>.
/// </summary>
internal sealed class TaskNameCache
{
    private const int CacheTtlSeconds = 60;
    private const int DefaultRecentRunLimit = 5000;

    private sealed record Entry(string[] Values, DateTime ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _cache = new();

    /// <summary>
    /// Returns the distinct recent task names for <paramref name="queueName"/>,
    /// serving from cache when available and not expired.
    /// </summary>
    internal async Task<string[]> GetOrFetchAsync(
        NpgsqlDataSource dataSource,
        string queueName,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (_cache.TryGetValue(queueName, out var entry) && now < entry.ExpiresAt)
            return entry.Values;

        var names = await FetchAsync(dataSource, queueName, DefaultRecentRunLimit, ct);
        _cache[queueName] = new Entry(names, now.AddSeconds(CacheTtlSeconds));
        return names;
    }

    private static async Task<string[]> FetchAsync(
        NpgsqlDataSource dataSource,
        string queueName,
        int limit,
        CancellationToken ct)
    {
        var ttable = QueueHelpers.QueueTableIdentifier("t", queueName);
        var rtable = QueueHelpers.QueueTableIdentifier("r", queueName);

        var sql = $"""
            WITH recent_runs AS (
                SELECT task_id
                FROM absurd.{rtable}
                ORDER BY run_id DESC
                LIMIT $1
            )
            SELECT DISTINCT t.task_name
            FROM recent_runs r
            JOIN absurd.{ttable} t ON t.task_id = r.task_id
            WHERE t.task_name <> ''
            ORDER BY t.task_name
            """;

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue(limit);

        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0).Trim();
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }

        return [.. names];
    }
}

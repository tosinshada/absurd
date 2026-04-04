namespace Absurd.Dashboard;

/// <summary>
/// Configuration options for the Absurd Dashboard middleware.
/// </summary>
public sealed class DashboardOptions
{
    /// <summary>
    /// The PostgreSQL connection string used by the dashboard to query Absurd tables.
    /// Required — an <see cref="InvalidOperationException"/> is thrown at startup when empty.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}

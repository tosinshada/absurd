using Absurd.Dashboard.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Absurd.Dashboard.DependencyInjection;

/// <summary>
/// Extension methods for registering Absurd Dashboard services with an
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class DashboardServiceCollectionExtensions
{
    /// <summary>
    /// Key used to register and resolve the dashboard's <see cref="NpgsqlDataSource"/>
    /// as a keyed singleton, avoiding collisions with any application-level data sources.
    /// </summary>
    internal const string ServiceKey = "absurd.dashboard";

    /// <summary>
    /// Registers the Absurd Dashboard services, configuring options via the supplied delegate.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddAbsurdDashboard(opts =>
    /// {
    ///     opts.ConnectionString = "Host=localhost;Database=mydb";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddAbsurdDashboard(
        this IServiceCollection services,
        Action<DashboardOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.Configure(configure);
        return AddCore(services);
    }

    /// <summary>
    /// Registers the Absurd Dashboard services, binding options from the supplied
    /// <see cref="IConfiguration"/> section.
    /// </summary>
    /// <example>
    /// <code>
    /// // appsettings.json: { "AbsurdDashboard": { "ConnectionString": "..." } }
    /// builder.Services.AddAbsurdDashboard(configuration.GetSection("AbsurdDashboard"));
    /// </code>
    /// </example>
    public static IServiceCollection AddAbsurdDashboard(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<DashboardOptions>(configuration);
        return AddCore(services);
    }

    private static IServiceCollection AddCore(IServiceCollection services)
    {
        // Register the data source as keyed to avoid collisions with any NpgsqlDataSource
        // that the host application or Absurd.Sdk may have registered.
        services.AddKeyedSingleton<NpgsqlDataSource>(ServiceKey, (sp, _) =>
        {
            var options = sp.GetRequiredService<IOptions<DashboardOptions>>().Value;

            // Fail-fast: surface misconfiguration at startup rather than on first request.
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
                throw new InvalidOperationException(
                    "Absurd.Dashboard requires a PostgreSQL connection string. " +
                    "Configure it via AddAbsurdDashboard(opts => opts.ConnectionString = \"Host=...;Database=...;\").");

            return NpgsqlDataSource.Create(options.ConnectionString);
        });

        // Register handler as singleton; resolved from the DI container in the pipeline branch.
        services.AddSingleton<DashboardHandler>(sp =>
        {
            var dataSource = sp.GetRequiredKeyedService<NpgsqlDataSource>(ServiceKey);
            return new DashboardHandler(dataSource);
        });

        return services;
    }
}

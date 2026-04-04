using Absurd.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Absurd.DependencyInjection;

/// <summary>
/// Extension methods for registering <see cref="AbsurdClient"/> with an
/// <see cref="IServiceCollection"/>.
/// </summary>
public static class AbsurdServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AbsurdClient"/> and <see cref="IAbsurdClient"/> as singletons,
    /// configuring options via the supplied delegate.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddAbsurd(opts =>
    /// {
    ///     opts.ConnectionString = "Host=localhost;Database=mydb";
    ///     opts.QueueName = "payments";
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddAbsurd(
        this IServiceCollection services,
        Action<AbsurdOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.Configure(configure);
        return AddAbsurdCore(services);
    }

    /// <summary>
    /// Registers <see cref="AbsurdClient"/> and <see cref="IAbsurdClient"/> as singletons,
    /// binding options from the supplied <see cref="IConfiguration"/> section.
    /// </summary>
    /// <example>
    /// <code>
    /// // appsettings.json: { "Absurd": { "ConnectionString": "...", "QueueName": "orders" } }
    /// services.AddAbsurd(configuration.GetSection("Absurd"));
    /// </code>
    /// </example>
    public static IServiceCollection AddAbsurd(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<AbsurdOptions>(configuration);
        return AddAbsurdCore(services);
    }

    private static IServiceCollection AddAbsurdCore(IServiceCollection services)
    {
        services.AddSingleton<AbsurdClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AbsurdOptions>>().Value;

            if (options.Log is null)
            {
                var loggerFactory = sp.GetService<ILoggerFactory>();
                if (loggerFactory is not null)
                    options.Log = loggerFactory.CreateLogger<AbsurdClient>();
            }

            return new AbsurdClient(options);
        });

        services.AddSingleton<IAbsurdClient>(sp => sp.GetRequiredService<AbsurdClient>());

        return services;
    }
}

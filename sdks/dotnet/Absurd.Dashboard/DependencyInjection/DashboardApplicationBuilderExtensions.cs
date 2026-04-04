using Absurd.Dashboard.Handlers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Absurd.Dashboard.DependencyInjection;

/// <summary>
/// Extension methods for mounting the Absurd Dashboard into an ASP.NET Core pipeline.
/// </summary>
public static class DashboardApplicationBuilderExtensions
{
    /// <summary>
    /// Mounts the Absurd Dashboard at the specified path prefix.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="pathPrefix">
    /// The path prefix under which the dashboard is served, e.g. <c>"/habitat"</c>.
    /// Must start with <c>/</c>. Defaults to <c>"/habitat"</c>.
    /// </param>
    /// <returns>The original application builder for chaining.</returns>
    /// <remarks>
    /// <para><see cref="DashboardServiceCollectionExtensions.AddAbsurdDashboard(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{DashboardOptions})"/>
    /// must be called before this method.</para>
    /// <para>The dashboard is isolated in its own pipeline branch via <c>app.Map</c>.
    /// Inside the branch, <c>Request.PathBase</c> reflects the mounted prefix so that all
    /// runtime config URLs generated for the SPA are correct.</para>
    /// </remarks>
    public static IApplicationBuilder MapAbsurdDashboard(
        this IApplicationBuilder app,
        string pathPrefix = "/habitat")
    {
        ArgumentException.ThrowIfNullOrEmpty(pathPrefix);
        if (!pathPrefix.StartsWith('/'))
            throw new ArgumentException("pathPrefix must start with '/'.", nameof(pathPrefix));

        return app.Map(pathPrefix, branch =>
        {
            branch.Run(async context =>
            {
                var handler = context.RequestServices.GetRequiredService<DashboardHandler>();
                await handler.HandleAsync(context);
            });
        });
    }
}

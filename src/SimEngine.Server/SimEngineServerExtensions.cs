using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SimEngine.Server;

/// <summary>
/// Extension methods for configuring the SimEngine Orleans silo.
/// </summary>
public static class SimEngineServerExtensions
{
    /// <summary>
    /// Adds SimEngine server services (local engine provider, etc.) to the host.
    /// </summary>
    public static IHostBuilder UseSimEngineSilo(this IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseOrleans(silo =>
        {
            silo.UseLocalhostClustering();
        });

        builder.ConfigureServices(services => services.AddSimEngineServer());

        return builder;
    }

    /// <summary>
    /// Registers SimEngine server services on an existing service collection.
    /// Used by <see cref="UseSimEngineSilo"/> and by test silo configurators.
    /// </summary>
    public static IServiceCollection AddSimEngineServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ILocalEngineProvider, LocalEngineProvider>();
        return services;
    }
}

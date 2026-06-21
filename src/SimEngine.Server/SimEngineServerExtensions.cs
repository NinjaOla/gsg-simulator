using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SimEngine.Server;

/// <summary>
/// Extension methods for configuring the SimEngine Orleans silo.
/// </summary>
public static class SimEngineServerExtensions
{
    private const string PubSubStoreName = "PubSubStore";

    /// <summary>
    /// Adds a SimEngine silo to the host. With no options the silo uses Orleans
    /// localhost clustering on its default ports with the session stream enabled
    /// — suitable for in-process single-player and for a loopback network host.
    /// Pass <paramref name="configure"/> to listen on custom ports (network mode).
    /// </summary>
    public static IHostBuilder UseSimEngineSilo(
        this IHostBuilder builder,
        Action<SimEngineSiloOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new SimEngineSiloOptions();
        configure?.Invoke(options);

        builder.UseOrleans(silo =>
        {
            silo.UseLocalhostClustering(
                siloPort: options.SiloPort,
                gatewayPort: options.GatewayPort);

            if (options.EnableStreams)
            {
                silo.AddMemoryStreams(Contracts.SessionStreams.ProviderName)
                    .AddMemoryGrainStorage(PubSubStoreName);
            }
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

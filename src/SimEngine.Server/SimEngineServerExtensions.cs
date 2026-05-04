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

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<ILocalEngineProvider, LocalEngineProvider>();
        });

        return builder;
    }
}

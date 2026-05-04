using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimEngine.Contracts;

namespace SimEngine.Client;

/// <summary>
/// Provides helpers for connecting to a game session silo (local or remote).
/// </summary>
public static class GameClient
{
    /// <summary>
    /// Creates an Orleans client connected to a local (localhost) silo cluster.
    /// </summary>
    public static async Task<IClusterClient> ConnectLocalAsync(CancellationToken ct = default)
    {
        var host = new HostBuilder()
            .UseOrleansClient(client =>
            {
                client.UseLocalhostClustering();
            })
            .Build();

        await host.StartAsync(ct);
        return host.Services.GetRequiredService<IClusterClient>();
    }

    /// <summary>
    /// Gets the game session grain for the given session ID.
    /// </summary>
    public static IGameSessionGrain GetSession(IClusterClient client, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return client.GetGrain<IGameSessionGrain>(sessionId);
    }
}

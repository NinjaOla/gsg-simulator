using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimEngine.Contracts;
using SimEngine.Server;
using Xunit;

namespace SimEngine.Server.Tests;

/// <summary>
/// Proves the network hosting mode: <c>UseSimEngineSilo</c> with custom ports
/// stands up a silo that actually listens on its client gateway, and the
/// co-hosted grain runs a real session. Uses uncommon loopback ports to avoid
/// colliding with a running silo or debug session on the Orleans defaults.
/// </summary>
public sealed class NetworkSiloHostingTests : IAsyncLifetime
{
    private const int SiloPort = 11321;
    private const int GatewayPort = 30321;
    private const string WorldId = "grid4";
    private static readonly DateTimeOffset StartDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private IHost _host = null!;

    public async ValueTask InitializeAsync()
    {
        _host = new HostBuilder()
            .UseSimEngineSilo(o =>
            {
                o.SiloPort = SiloPort;
                o.GatewayPort = GatewayPort;
            })
            .Build();

        await _host.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task Silo_ListensOnConfiguredGatewayPort()
    {
        using var tcp = new TcpClient();

        await tcp.ConnectAsync("127.0.0.1", GatewayPort, TestContext.Current.CancellationToken);

        Assert.True(tcp.Connected);
    }

    [Fact]
    public async Task CoHostedGrain_RunsSession_OverNetworkSilo()
    {
        var factory = _host.Services.GetRequiredService<IGrainFactory>();
        var grain = factory.GetGrain<IGameSessionGrain>("network-session");

        await grain.InitializeAsync(WorldId, StartDate, seed: 42);
        var result = await grain.StepAsync(1);

        Assert.Equal(1, result.TickNumber);
    }
}

using Akka.Actor;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Networking;
using SimEngine.Networking.InProc;
using SimEngine.Networking.Tcp;
using SimEngine.Server.Direct;
using SimEngine.Server.Gateway;

namespace SimEngine.Server.Tests;

/// <summary>
/// Shared per-test host that stands up the SimEngine server actor topology in
/// local (single-node) mode plus a gateway (in-process or TCP loopback). Exposes trusted
/// Akka-level access (<see cref="Client"/>, raw entity hosts) and untrusted
/// wire clients via <see cref="ConnectAsync"/>. One instance per test keeps sessions isolated.
/// </summary>
public sealed class ServerTestHarness : IAsyncDisposable
{
    private static readonly TransportEndpoint InProcEndpoint = new(InProcTransport.SchemeName, "server");

    private readonly IHost _host;
    private readonly ITransport _transport;
    private readonly TransportEndpoint _endpoint;
    private readonly List<GameClient> _wireClients = [];

    private ServerTestHarness(IHost host, ITransport transport, TransportEndpoint endpoint, string saveDirectory)
    {
        _host = host;
        _transport = transport;
        _endpoint = endpoint;
        SaveDirectory = saveDirectory;
        System = host.Services.GetRequiredService<ActorSystem>();
        var registry = host.Services.GetRequiredService<ActorRegistry>();
        Sessions = registry.Get<SessionActorsMarker>();
        Players = registry.Get<PlayerActorsMarker>();
        Client = ActorGameClient.FromLocalRegistry(System, registry);
    }

    /// <summary>The backing actor system.</summary>
    public ActorSystem System { get; }

    /// <summary>The game-session entity host.</summary>
    public IActorRef Sessions { get; }

    /// <summary>The player entity host.</summary>
    public IActorRef Players { get; }

    /// <summary>Trusted Akka-level access to the server actors.</summary>
    public ActorGameClient Client { get; }

    /// <summary>The gateway's save directory (unique per harness).</summary>
    public string SaveDirectory { get; }

    /// <summary>Builds and starts a fresh harness with default session options.</summary>
    public static Task<ServerTestHarness> StartAsync(CancellationToken ct) => StartAsync(null, ct);

    /// <summary>Builds and starts a fresh harness.</summary>
    public static Task<ServerTestHarness> StartAsync(
        SessionRuntimeOptions? sessionOptions = null,
        CancellationToken ct = default) =>
        StartAsync(new InProcTransport(), InProcEndpoint, sessionOptions, ct);

    /// <summary>Builds and starts a fresh harness whose gateway listens on TCP loopback (ephemeral port).</summary>
    public static Task<ServerTestHarness> StartTcpAsync(CancellationToken ct) =>
        StartAsync(new TcpTransport(), TcpTransport.CreateEndpoint("127.0.0.1", 0), null, ct);

    private static async Task<ServerTestHarness> StartAsync(
        ITransport transport,
        TransportEndpoint endpoint,
        SessionRuntimeOptions? sessionOptions,
        CancellationToken ct)
    {
        var listener = await transport.ListenAsync(endpoint, ct);
        var saveDirectory = Path.Combine(Path.GetTempPath(), "simengine-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(saveDirectory);

        var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddAkka($"SimEngineTest{Guid.NewGuid():N}", (builder, _) =>
                {
                    builder
                        .WithSimEngineActors(AkkaExecutionMode.LocalTest, sessionOptions: sessionOptions)
                        .WithSimEngineGateway(listener, new GatewayOptions { SaveDirectory = saveDirectory });
                });
            })
            .Build();

        await host.StartAsync(ct);
        return new ServerTestHarness(host, transport, listener.LocalEndpoint, saveDirectory);
    }

    /// <summary>Connects an untrusted wire client through the gateway.</summary>
    public async Task<GameClient> ConnectAsync(string playerName, CancellationToken ct)
    {
        var client = await GameClient.ConnectAsync(new TransportRegistry(_transport), _endpoint, playerName, ct);
        _wireClients.Add(client);
        return client;
    }

    /// <summary>Opens a raw transport connection to the gateway (no handshake).</summary>
    public Task<ITransportConnection> ConnectRawAsync(CancellationToken ct) => _transport.ConnectAsync(_endpoint, ct);

    /// <summary>Subscribes a test actor to a session at the Akka level.</summary>
    public Task<ActorSessionSubscription> SubscribeAsync(ActorSessionClient session, CancellationToken ct) =>
        ActorSessionSubscription.StartAsync(System, session, ct);

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _wireClients)
        {
            await client.DisposeAsync();
        }

        await _host.StopAsync();
        _host.Dispose();
        try
        {
            Directory.Delete(SaveDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

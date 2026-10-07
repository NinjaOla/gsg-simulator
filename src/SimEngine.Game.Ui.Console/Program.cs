using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Game.Ui.Console;
using SimEngine.Game.Ui.Console.Game;
using SimEngine.Networking;
using SimEngine.Networking.InProc;
using SimEngine.Server;
using SimEngine.Server.Gateway;

if (ServerMode.IsRequested(args))
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    return await ServerMode.RunAsync(args, cts.Token);
}

// Single-player: a host-owned server in this process, reached through the same
// gateway and wire protocol as multiplayer (in-process transport).
var transport = new InProcTransport();
var endpoint = new TransportEndpoint(InProcTransport.SchemeName, "local");
var listener = await transport.ListenAsync(endpoint);

var host = new HostBuilder()
    .ConfigureServices(services =>
    {
        services.AddAkka("SimEngine", (builder, _) =>
        {
            builder
                .WithSimEngineActors(AkkaExecutionMode.LocalTest)
                .WithSimEngineGateway(listener, new GatewayOptions
                {
                    SaveDirectory = SaveGamePaths.SavesDirectory,
                    AllowArbitrarySavePaths = true,
                });
        });
        // Resolved after the host has started, so the gateway is accepting.
        services.AddSingleton(_ =>
            GameClient.ConnectAsync(new TransportRegistry(transport), endpoint, PlayerProfile.GetName())
                .GetAwaiter().GetResult());
    })
    .Build();

await host.StartAsync();

try
{
    App.Run(host.Services);
}
finally
{
    await host.Services.GetRequiredService<GameClient>().DisposeAsync();
    await host.StopAsync();
}

return 0;

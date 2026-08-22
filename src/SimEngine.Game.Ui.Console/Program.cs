using Akka.Actor;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Game.Ui.Console;
using SimEngine.Server;

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

var host = new HostBuilder()
    .ConfigureServices(services =>
    {
        services.AddSimEngineServer();
        services.AddAkka("SimEngine", (builder, _) =>
        {
            builder.WithSimEngineActors(AkkaExecutionMode.LocalTest);
        });
        services.AddSingleton(sp =>
            GameClient.FromLocalRegistry(
                sp.GetRequiredService<ActorSystem>(),
                sp.GetRequiredService<ActorRegistry>()));
    })
    .Build();

await host.StartAsync();

try
{
    App.Run(host.Services);
}
finally
{
    await host.StopAsync();
}

return 0;

using Microsoft.Extensions.Hosting;
using SimEngine.ConsoleHost;
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
    .UseSimEngineSilo()
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

using Microsoft.Extensions.Hosting;
using SimEngine.Server;

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

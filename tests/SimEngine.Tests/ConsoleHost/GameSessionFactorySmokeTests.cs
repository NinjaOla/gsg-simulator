using Microsoft.Extensions.Hosting;
using SimEngine.ConsoleHost.Game;
using SimEngine.Server;
using Xunit;

namespace SimEngine.Tests.ConsoleHost;

/// <summary>
/// End-to-end smoke of the console host wiring below the prompt layer:
/// in-process silo up, new game via the grain, read view reflecting grain
/// mutations, save/load round trip, event log fed by game events.
/// </summary>
public sealed class GameSessionFactorySmokeTests : IAsyncLifetime
{
    private static readonly DateTimeOffset StartDate = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private IHost _host = null!;

    public async ValueTask InitializeAsync()
    {
        _host = new HostBuilder()
            .UseSimEngineSilo()
            .Build();
        await _host.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public async Task NewGame_Step_Save_Load_Step_FlowsThroughGrain()
    {
        var savePath = $"smoke-{Guid.NewGuid():N}";
        string? resolvedSavePath = null;
        try
        {
            // New game.
            using var session = GameSessionFactory.CreateNew(
                _host.Services, "grid4", StartDate, seed: 42);

            Assert.Equal(4, session.ProvinceCount);
            Assert.True(session.AdjacencyEdgeCount > 0);

            // Mutate via grain; the in-process read view must reflect it.
            await session.Grain.StepAsync(40);
            Assert.Equal(40, session.Engine.TickNumber);
            Assert.True(session.Engine.Time.GetUtcNow() > StartDate);

            // Stepping past a month boundary publishes game events into the log.
            Assert.Contains(session.EventLog, entry => entry.Contains("collected", StringComparison.Ordinal));

            // Save.
            resolvedSavePath = GameSessionFactory.Save(session, savePath);
            Assert.True(File.Exists(resolvedSavePath));

            // Load into a fresh session and continue.
            using var loaded = GameSessionFactory.Load(savePath, _host.Services);
            Assert.Equal(40, loaded.Engine.TickNumber);
            Assert.Equal(4, loaded.ProvinceCount);
            Assert.Equal(session.WorldName, loaded.WorldName);

            await loaded.Grain.StepAsync(5);
            Assert.Equal(45, loaded.Engine.TickNumber);
        }
        finally
        {
            if (resolvedSavePath is not null && File.Exists(resolvedSavePath))
            {
                File.Delete(resolvedSavePath);
            }
        }
    }
}

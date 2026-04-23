using SimEngine;
using SimEngine.ConsoleHost.Events;
using SimEngine.Systems;
using SimEngine.Time;

namespace SimEngine.ConsoleHost.Systems;

public sealed class HeartbeatSystem : ISimulationSystem
{
    public string Name => "Heartbeat";
    public TickCadence Cadence => TickCadence.Monthly;
    public int Order => 0;
    public IReadOnlyCollection<StateKey> Reads => [];
    public IReadOnlyCollection<StateKey> Writes => [];

    public void Execute(in SimulationContext ctx) =>
        ctx.Events.Publish(new HeartbeatEvent(ctx.TickEnd, ctx.State.Entities.Count));
}

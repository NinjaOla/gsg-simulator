using SimEngine;
using SimEngine.Game.Components;
using SimEngine.State;
using SimEngine.Systems;
using SimEngine.Time;

namespace SimEngine.Game.Systems;

public sealed class PopulationSystem : ISimulationSystem
{
    public string Name => "Population";
    public string Key => "game.population.v1";
    public TickCadence Cadence => TickCadence.Monthly;
    public int Order => 10;
    public IReadOnlyCollection<StateKey> Reads => [ComponentStateKeys.Of<PopulationComponent>()];
    public IReadOnlyCollection<StateKey> Writes => [ComponentStateKeys.Of<PopulationComponent>()];

    public void Execute(in SimulationContext ctx)
    {
        foreach (var (id, pop) in ctx.State.Entities.Query<PopulationComponent>())
        {
            // Monthly growth = annual_rate / 12, all in fixed-point integer math.
            var delta = pop.Population * pop.GrowthRateE6 / 12_000_000L;
            ref var component = ref ctx.State.Entities.GetRef<PopulationComponent>(id);
            component = component with { Population = component.Population + delta };
        }
    }
}

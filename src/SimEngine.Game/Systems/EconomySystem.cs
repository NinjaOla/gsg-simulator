using SimEngine;
using SimEngine.Game.Components;
using SimEngine.Game.Events;
using SimEngine.State;
using SimEngine.Systems;
using SimEngine.Time;

namespace SimEngine.Game.Systems;

public sealed class EconomySystem : ISimulationSystem
{
    public string Name => "Economy";
    public string Key => "game.economy.v1";
    public TickCadence Cadence => TickCadence.Monthly;
    public int Order => 20;
    public IReadOnlyCollection<StateKey> Reads =>
    [
        ComponentStateKeys.Of<EconomyComponent>(),
        ComponentStateKeys.Of<CountryComponent>(),
    ];
    public IReadOnlyCollection<StateKey> Writes => [ComponentStateKeys.Of<TreasuryComponent>()];

    public void Execute(in SimulationContext ctx)
    {
        foreach (var (countryId, _) in ctx.State.Entities.Query<TreasuryComponent>())
        {
            var income = 0L;
            foreach (var provinceId in ctx.State.Relationships.GetOutbound(countryId, RelationshipLabel.Owns))
            {
                if (ctx.State.Entities.TryGet<EconomyComponent>(provinceId, out var eco))
                {
                    income += eco.ProductionE2;
                }
            }

            ref var treasury = ref ctx.State.Entities.GetRef<TreasuryComponent>(countryId);
            treasury = treasury with { FundsE2 = treasury.FundsE2 + income };

            var tag = ctx.State.Entities.TryGet<CountryComponent>(countryId, out var country)
                ? country.Tag
                : countryId.ToString();
            ctx.Events.Publish(new IncomeCollectedEvent(countryId, tag, income, treasury.FundsE2, ctx.TickEnd));
        }
    }
}

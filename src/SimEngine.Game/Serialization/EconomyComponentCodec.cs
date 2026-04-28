using System.Text.Json;
using SimEngine.Game.Components;
using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Serialization;

namespace SimEngine.Game.Serialization;

public sealed class EconomyComponentCodec : IComponentSectionCodec
{
    public string SectionType => "SimEngine.Game.Components.EconomyComponent";

    public JsonElement WriteSection(SimulationState state, JsonSerializerOptions options)
    {
        var entries = state.Entities.Query<EconomyComponent>()
            .Select(e => new EconomyEntrySnapshot(e.Id.Value, e.Component.ProductionE2))
            .ToArray();
        return JsonSerializer.SerializeToElement(entries, options);
    }

    public void ReadSection(SimulationState state, JsonElement payload, JsonSerializerOptions options)
    {
        var entries = payload.Deserialize<EconomyEntrySnapshot[]>(options)
            ?? throw new InvalidDataException("Economy component section payload was missing.");
        foreach (var entry in entries)
        {
            state.Entities.Attach(new EntityId(entry.EntityId), new EconomyComponent(entry.ProductionE2));
        }
    }
}

internal sealed record EconomyEntrySnapshot(uint EntityId, int ProductionE2);

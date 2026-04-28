using System.Text.Json;
using SimEngine.Game.Components;
using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Serialization;

namespace SimEngine.Game.Serialization;

public sealed class PopulationComponentCodec : IComponentSectionCodec
{
    public string SectionType => "SimEngine.Game.Components.PopulationComponent";

    public JsonElement WriteSection(SimulationState state, JsonSerializerOptions options)
    {
        var entries = state.Entities.Query<PopulationComponent>()
            .Select(e => new PopulationEntrySnapshot(e.Id.Value, e.Component.Population, e.Component.GrowthRateE6))
            .ToArray();
        return JsonSerializer.SerializeToElement(entries, options);
    }

    public void ReadSection(SimulationState state, JsonElement payload, JsonSerializerOptions options)
    {
        var entries = payload.Deserialize<PopulationEntrySnapshot[]>(options)
            ?? throw new InvalidDataException("Population component section payload was missing.");
        foreach (var entry in entries)
        {
            state.Entities.Attach(new EntityId(entry.EntityId), new PopulationComponent(entry.Population, entry.GrowthRateE6));
        }
    }
}

internal sealed record PopulationEntrySnapshot(uint EntityId, long Population, int GrowthRateE6);

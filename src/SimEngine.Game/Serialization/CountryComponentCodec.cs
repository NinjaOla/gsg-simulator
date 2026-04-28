using System.Text.Json;
using SimEngine.Game.Components;
using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Serialization;

namespace SimEngine.Game.Serialization;

public sealed class CountryComponentCodec : IComponentSectionCodec
{
    public string SectionType => "SimEngine.Game.Components.CountryComponent";

    public JsonElement WriteSection(SimulationState state, JsonSerializerOptions options)
    {
        var entries = state.Entities.Query<CountryComponent>()
            .Select(e => new CountryEntrySnapshot(e.Id.Value, e.Component.Tag, e.Component.DisplayName))
            .ToArray();
        return JsonSerializer.SerializeToElement(entries, options);
    }

    public void ReadSection(SimulationState state, JsonElement payload, JsonSerializerOptions options)
    {
        var entries = payload.Deserialize<CountryEntrySnapshot[]>(options)
            ?? throw new InvalidDataException("Country component section payload was missing.");
        foreach (var entry in entries)
        {
            state.Entities.Attach(new EntityId(entry.EntityId), new CountryComponent(entry.Tag, entry.DisplayName));
        }
    }
}

internal sealed record CountryEntrySnapshot(uint EntityId, string Tag, string DisplayName);

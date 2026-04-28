using System.Text.Json;
using SimEngine.Game.Components;
using SimEngine.Ids;
using SimEngine.State;
using SimEngine.State.Serialization;

namespace SimEngine.Game.Serialization;

public sealed class TreasuryComponentCodec : IComponentSectionCodec
{
    public string SectionType => "SimEngine.Game.Components.TreasuryComponent";

    public JsonElement WriteSection(SimulationState state, JsonSerializerOptions options)
    {
        var entries = state.Entities.Query<TreasuryComponent>()
            .Select(e => new TreasuryEntrySnapshot(e.Id.Value, e.Component.FundsE2))
            .ToArray();
        return JsonSerializer.SerializeToElement(entries, options);
    }

    public void ReadSection(SimulationState state, JsonElement payload, JsonSerializerOptions options)
    {
        var entries = payload.Deserialize<TreasuryEntrySnapshot[]>(options)
            ?? throw new InvalidDataException("Treasury component section payload was missing.");
        foreach (var entry in entries)
        {
            state.Entities.Attach(new EntityId(entry.EntityId), new TreasuryComponent(entry.FundsE2));
        }
    }
}

internal sealed record TreasuryEntrySnapshot(uint EntityId, long FundsE2);

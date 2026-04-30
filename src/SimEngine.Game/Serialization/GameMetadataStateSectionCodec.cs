using System.Text.Json;
using SimEngine.State;
using SimEngine.State.Serialization;

namespace SimEngine.Game.Serialization;

public sealed class GameMetadataStateSectionCodec : IStateSectionCodec
{
    public string SectionType => "SimEngine.Game.State.GameMetadata";

    public JsonElement WriteSection(SimulationState state, JsonSerializerOptions options)
    {
        var entries = state.Metadata
            .Select(entry => new GameMetadataEntrySnapshot(entry.Key, entry.Value))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();

        return JsonSerializer.SerializeToElement(entries, options);
    }

    public void ReadSection(SimulationState state, JsonElement payload, JsonSerializerOptions options)
    {
        var entries = payload.Deserialize<GameMetadataEntrySnapshot[]>(options)
            ?? throw new InvalidDataException("Game metadata section payload was missing.");

        state.Metadata.Clear();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
            {
                throw new InvalidDataException("Game metadata keys cannot be blank.");
            }

            state.Metadata[entry.Key] = entry.Value;
        }
    }
}

internal sealed record GameMetadataEntrySnapshot(string Key, string Value);

using System.Text.Json;

namespace SimEngine.State.Serialization;

internal static class SimulationComponentSections
{
    public const string ProvinceComponent = "SimEngine.State.Components.ProvinceComponent";
}

internal sealed record SimulationSaveFile(
    int FormatVersion,
    EngineSnapshot Engine,
    SaveMetadataEntrySnapshot[]? SaveMetadata);

internal sealed record EngineSnapshot(
    DateTimeOffset StartDate,
    DateTimeOffset CurrentTime,
    DateTimeOffset PreviousTick,
    long TickNumber,
    long DefaultTickDeltaTicks,
    ulong Seed,
    bool EnableParallelBatches,
    int? MaxDegreeOfParallelism,
    RandomSnapshot RootRandom,
    SystemRandomSnapshot[] SystemRandoms,
    SimulationStateSnapshot State);

internal sealed record RandomSnapshot(
    ulong S0,
    ulong S1,
    ulong S2,
    ulong S3);

internal sealed record SystemRandomSnapshot(
    string SystemKey,
    RandomSnapshot Random);

internal sealed record SimulationStateSnapshot(
    EntitySnapshot[] Entities,
    RelationshipSnapshot[] Relationships,
    AdjacencyNodeSnapshot[] Adjacency,
    ComponentSectionSnapshot[] ComponentSections,
    StateSectionSnapshot[]? StateSections);

internal sealed record EntitySnapshot(uint Id);

internal sealed record RelationshipSnapshot(
    uint FromEntityId,
    int Label,
    uint ToEntityId);

internal sealed record AdjacencyNodeSnapshot(
    uint ProvinceId,
    uint[] Neighbors);

internal sealed record ComponentSectionSnapshot(
    string SectionType,
    JsonElement Payload);

internal sealed record StateSectionSnapshot(
    string SectionType,
    JsonElement Payload);

internal sealed record SaveMetadataEntrySnapshot(
    string Key,
    string Value);

internal sealed record ProvinceComponentEntrySnapshot(
    uint EntityId,
    string Name,
    int Terrain,
    int CentroidLatE6,
    int CentroidLonE6);

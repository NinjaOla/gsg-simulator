using SimEngine.Game.Serialization;
using SimEngine.Game.Systems;
using SimEngine.State.Serialization;
using SimEngine.Systems;

namespace SimEngine.Game;

public sealed record GameManifest(
    string GameId,
    string ScenarioId,
    string ContentVersion,
    string ContentHash,
    IReadOnlyList<string> EnabledFeatures)
{
    internal IReadOnlyDictionary<string, string> ToSaveMetadata()
    {
        if (string.IsNullOrWhiteSpace(GameId))
        {
            throw new ArgumentException("GameId cannot be blank.", nameof(GameId));
        }

        if (string.IsNullOrWhiteSpace(ScenarioId))
        {
            throw new ArgumentException("ScenarioId cannot be blank.", nameof(ScenarioId));
        }

        if (string.IsNullOrWhiteSpace(ContentVersion))
        {
            throw new ArgumentException("ContentVersion cannot be blank.", nameof(ContentVersion));
        }

        if (string.IsNullOrWhiteSpace(ContentHash))
        {
            throw new ArgumentException("ContentHash cannot be blank.", nameof(ContentHash));
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gameId"] = GameId,
            ["scenarioId"] = ScenarioId,
            ["contentVersion"] = ContentVersion,
            ["contentHash"] = ContentHash,
            ["enabledFeatures"] = string.Join(",", EnabledFeatures.OrderBy(feature => feature, StringComparer.Ordinal)),
        };
    }
}

public sealed class GameDefinition
{
    public GameDefinition(
        GameManifest manifest,
        IReadOnlyList<ISimulationSystem> systems,
        IReadOnlyList<IComponentSectionCodec> componentCodecs,
        IReadOnlyList<IStateSectionCodec> stateSectionCodecs)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(systems);
        ArgumentNullException.ThrowIfNull(componentCodecs);
        ArgumentNullException.ThrowIfNull(stateSectionCodecs);

        Manifest = manifest;
        Systems = systems;
        ComponentCodecs = componentCodecs;
        StateSectionCodecs = stateSectionCodecs;
    }

    public GameManifest Manifest { get; }

    public IReadOnlyList<ISimulationSystem> Systems { get; }

    public IReadOnlyList<IComponentSectionCodec> ComponentCodecs { get; }

    public IReadOnlyList<IStateSectionCodec> StateSectionCodecs { get; }

    public IReadOnlyDictionary<string, string> SaveMetadata => Manifest.ToSaveMetadata();

    public static GameDefinition CreateDefault(
        string scenarioId,
        string contentVersion,
        string contentHash,
        IReadOnlyList<string>? enabledFeatures = null)
    {
        var manifest = new GameManifest(
            GameId: "simengine.game",
            ScenarioId: scenarioId,
            ContentVersion: contentVersion,
            ContentHash: contentHash,
            EnabledFeatures: enabledFeatures ?? []);

        return new GameDefinition(
            manifest,
            systems:
            [
                new PopulationSystem(),
                new EconomySystem(),
            ],
            componentCodecs: GameCodecs.All,
            stateSectionCodecs:
            [
                new GameMetadataStateSectionCodec(),
            ]);
    }
}

using SimEngine.ConsoleHost.Systems;
using SimEngine.Game;
using SimEngine.State;
using SimEngine.Systems;
using System.Text.Json;

namespace SimEngine.ConsoleHost.Game;

internal static class GameSessionFactory
{
    private const string DefaultContentVersion = "dev";
    private const string DefaultContentHash = "dev";
    private const string SaveMetadataPropertyName = "saveMetadata";

    public static GameDefinition CreateDefinitionForWorld(string worldName) =>
        GameDefinition.CreateDefault(
            scenarioId: worldName,
            contentVersion: DefaultContentVersion,
            contentHash: DefaultContentHash);

    public static GameSession CreateNew(
        SimulationState state,
        DateTimeOffset startDate,
        ulong seed,
        string worldName,
        GameDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldName);
        ArgumentNullException.ThrowIfNull(definition);

        state.Metadata["worldName"] = worldName;
        var engine = new SimulationEngine(
            new SimulationEngineOptions
            {
                StartDate = startDate,
                Seed = seed,
                InitialState = state,
                ComponentCodecs = definition.ComponentCodecs,
                StateSectionCodecs = definition.StateSectionCodecs,
                SaveMetadata = definition.SaveMetadata,
            },
            CreateSystems(definition));

        return new GameSession(engine, worldName, definition);
    }

    public static string Save(GameSession session, string path)
    {
        ArgumentNullException.ThrowIfNull(session);

        var resolvedPath = SaveGamePaths.Resolve(path);
        session.Engine.Save(resolvedPath);
        return resolvedPath;
    }

    public static GameSession Load(string path)
    {
        var resolvedPath = SaveGamePaths.Resolve(path);
        var definition = CreateDefinitionFromSave(resolvedPath);
        return Load(resolvedPath, definition);
    }

    public static GameSession Load(string path, GameDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var resolvedPath = SaveGamePaths.Resolve(path);
        var engine = SimulationEngine.Load(
            resolvedPath,
            CreateSystems(definition),
            definition.ComponentCodecs,
            definition.StateSectionCodecs,
            definition.SaveMetadata);

        var worldName = engine.State.Metadata.TryGetValue("worldName", out var savedWorldName)
            && !string.IsNullOrWhiteSpace(savedWorldName)
            ? savedWorldName
            : SaveGamePaths.GetDisplayName(resolvedPath);

        return new GameSession(engine, worldName, definition);
    }

    private static ISimulationSystem[] CreateSystems(GameDefinition definition) =>
    [
        new HeartbeatSystem(),
        ..definition.Systems,
    ];

    private static GameDefinition CreateDefinitionFromSave(string resolvedPath)
    {
        using var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var document = JsonDocument.Parse(stream);

        var root = document.RootElement;
        if (!root.TryGetProperty(SaveMetadataPropertyName, out var saveMetadataElement)
            || saveMetadataElement.ValueKind != JsonValueKind.Array)
        {
            return CreateDefinitionForWorld("default");
        }

        var entries = saveMetadataElement.EnumerateArray()
            .Where(element => element.ValueKind == JsonValueKind.Object)
            .Select(element => (
                Key: element.TryGetProperty("key", out var key) ? key.GetString() : null,
                Value: element.TryGetProperty("value", out var value) ? value.GetString() : null))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && entry.Value is not null)
            .ToDictionary(entry => entry.Key!, entry => entry.Value!, StringComparer.Ordinal);

        if (!entries.TryGetValue(GameManifestMetadata.ScenarioIdKey, out var scenarioId))
        {
            scenarioId = "default";
        }

        entries.TryGetValue(GameManifestMetadata.ContentVersionKey, out var contentVersion);
        entries.TryGetValue(GameManifestMetadata.ContentHashKey, out var contentHash);

        var enabledFeatures = entries.TryGetValue(GameManifestMetadata.EnabledFeaturesKey, out var featureCsv)
            && !string.IsNullOrWhiteSpace(featureCsv)
            ? featureCsv.Split(GameManifestMetadata.FeatureSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];

        return GameDefinition.CreateDefault(
            scenarioId: scenarioId,
            contentVersion: string.IsNullOrWhiteSpace(contentVersion) ? DefaultContentVersion : contentVersion,
            contentHash: string.IsNullOrWhiteSpace(contentHash) ? DefaultContentHash : contentHash,
            enabledFeatures: enabledFeatures);
    }
}

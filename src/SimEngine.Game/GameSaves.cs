using System.Text.Json;

namespace SimEngine.Game;

/// <summary>
/// Reads the game-defined save manifest out of a save file and reconstructs
/// the <see cref="GameDefinition"/> required to load it. Any host (console,
/// server grain, tooling) goes through this so save compatibility rules live
/// in one place.
/// </summary>
public static class GameSaves
{
    private const string SaveMetadataPropertyName = "saveMetadata";
    private const string DefaultScenarioId = "default";
    private const string DefaultContentVersion = "dev";
    private const string DefaultContentHash = "dev";

    public static GameDefinition ReadDefinition(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var document = JsonDocument.Parse(stream);

        var root = document.RootElement;
        if (!root.TryGetProperty(SaveMetadataPropertyName, out var saveMetadataElement)
            || saveMetadataElement.ValueKind != JsonValueKind.Array)
        {
            return GameDefinition.CreateDefault(DefaultScenarioId, DefaultContentVersion, DefaultContentHash);
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
            scenarioId = DefaultScenarioId;
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

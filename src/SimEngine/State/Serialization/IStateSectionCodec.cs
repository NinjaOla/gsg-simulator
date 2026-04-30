using System.Text.Json;

namespace SimEngine.State.Serialization;

/// <summary>
/// Pluggable serialization codec for non-component game state sections.
/// Register via <see cref="SimulationEngineOptions.StateSectionCodecs"/>.
/// </summary>
public interface IStateSectionCodec
{
    /// <summary>
    /// Fully-qualified section type name written into the save file. Must be
    /// unique across all registered codecs. Changing this value invalidates
    /// existing saves.
    /// </summary>
    string SectionType { get; }

    /// <summary>
    /// When true, loading fails if this section is missing from the save file.
    /// </summary>
    bool IsRequired => false;

    /// <summary>Serializes section data from <paramref name="state"/> to a JSON element.</summary>
    JsonElement WriteSection(SimulationState state, JsonSerializerOptions options);

    /// <summary>Deserializes section data from <paramref name="payload"/> into <paramref name="state"/>.</summary>
    void ReadSection(SimulationState state, JsonElement payload, JsonSerializerOptions options);
}

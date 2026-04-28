using System.Text.Json;

namespace SimEngine.State.Serialization;

/// <summary>
/// Pluggable serialization codec for a single component type. Register via
/// <see cref="SimulationEngineOptions.ComponentCodecs"/>. The engine always
/// handles <c>ProvinceComponent</c> internally; game assemblies implement this
/// interface for any additional component types they want to persist.
/// </summary>
public interface IComponentSectionCodec
{
    /// <summary>
    /// Fully-qualified section type name written into the save file. Must be
    /// unique across all registered codecs. Changing this value invalidates
    /// existing saves.
    /// </summary>
    string SectionType { get; }

    /// <summary>Serializes all relevant components from <paramref name="state"/> to a JSON element.</summary>
    JsonElement WriteSection(SimulationState state, JsonSerializerOptions options);

    /// <summary>Deserializes component data from <paramref name="payload"/> and attaches components to <paramref name="state"/>.</summary>
    void ReadSection(SimulationState state, JsonElement payload, JsonSerializerOptions options);
}

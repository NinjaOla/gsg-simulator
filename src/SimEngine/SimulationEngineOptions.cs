using SimEngine.State;
using SimEngine.State.Serialization;

namespace SimEngine;

/// <summary>Construction-time options for a <see cref="SimulationEngine"/>.</summary>
public sealed class SimulationEngineOptions
{
    public required DateTimeOffset StartDate { get; init; }

    public required ulong Seed { get; init; }

    /// <summary>Default delta applied by the parameterless <c>Step()</c>.</summary>
    public TimeSpan DefaultTickDelta { get; init; } = TimeSpan.FromDays(1);

    /// <summary>When true, systems in the same batch run concurrently.</summary>
    public bool EnableParallelBatches { get; init; } = true;

    /// <summary>
    /// Upper bound on batch parallelism. <c>null</c> means
    /// <see cref="Environment.ProcessorCount"/>.
    /// </summary>
    public int? MaxDegreeOfParallelism { get; init; }

    /// <summary>
    /// Optional pre-built state. When non-null, the engine takes ownership
    /// of the passed instance (including its entities, relationships, and
    /// adjacency graph) instead of constructing an empty one. Typical flow:
    /// build a world with <see cref="WorldBuilder"/>, pass the result here.
    /// </summary>
    public SimulationState? InitialState { get; init; }

    /// <summary>
    /// Pluggable codecs for additional component types beyond the built-in
    /// <c>ProvinceComponent</c>. Each codec handles one section type in the
    /// save file. Order does not matter; section types must be unique.
    /// </summary>
    public IReadOnlyList<IComponentSectionCodec> ComponentCodecs { get; init; } = [];
}

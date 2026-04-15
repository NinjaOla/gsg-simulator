namespace SimEngine.State.Components;

/// <summary>
/// Province data attached to an entity to mark it as a province.
/// All fields are integer-safe — no float/double in game state.
/// Centroid is stored as signed microdegrees (latitude/longitude × 1e6),
/// which fits comfortably in <see cref="int"/> (max |180_000_000|).
/// Neighbors live in <see cref="AdjacencyGraph"/>, not here — single source of truth.
/// </summary>
public readonly record struct ProvinceComponent(
    string Name,
    Terrain Terrain,
    int CentroidLatE6,
    int CentroidLonE6);

namespace SimEngine.State.Components;

/// <summary>
/// Coarse terrain classification for a province. Determines what kind of
/// movement and gameplay rules apply. Expanded terrain kinds (mountain,
/// forest, desert) are game-scope and would live as additional components
/// rather than enum values here.
/// </summary>
public enum Terrain : byte
{
    Land = 0,
    Sea = 1,
}

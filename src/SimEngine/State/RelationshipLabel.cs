namespace SimEngine.State;

/// <summary>
/// Edge labels for the entity relationship graph. Games and mods can extend
/// this by casting custom integer values — the engine only cares that the
/// label is a stable sortable int. Engine-reserved values live in the
/// 0–999 range; game code should start at 1000.
/// </summary>
public enum RelationshipLabel : int
{
    None = 0,
    Owns = 1,
    Vassal = 2,
    AtWarWith = 3,
    HasCapital = 4,
}

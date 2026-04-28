namespace SimEngine.Game.Components;

/// <summary>
/// Marks an entity as a country. <see cref="Tag"/> is the stable short
/// identifier (e.g. "DEU"); <see cref="DisplayName"/> is localisation-friendly.
/// </summary>
public readonly record struct CountryComponent(string Tag, string DisplayName);

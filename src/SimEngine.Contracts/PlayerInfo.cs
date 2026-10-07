namespace SimEngine.Contracts;

/// <summary>A session member as shown in lobby and game screens.</summary>
/// <param name="Id">Server-assigned player id.</param>
/// <param name="Name">Display name chosen by the player.</param>
/// <param name="Connected"><c>false</c> once the player's connection dropped after the game started.</param>
public sealed record PlayerInfo(string Id, string Name, bool Connected);

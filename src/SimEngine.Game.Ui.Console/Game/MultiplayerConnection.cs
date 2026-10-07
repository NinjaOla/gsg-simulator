using SimEngine.Client;

namespace SimEngine.Game.Ui.Console.Game;

/// <summary>
/// A multiplayer client connection, plus the child server when this player hosts.
/// Owned by the lobby/game session and disposed when the player leaves.
/// </summary>
internal sealed class MultiplayerConnection(GameClient client, ServerProcess? server = null) : IAsyncDisposable
{
    public ServerProcess? Server => server;

    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync().ConfigureAwait(false);
        if (server is not null)
        {
            await server.DisposeAsync().ConfigureAwait(false);
        }
    }
}

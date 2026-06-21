using SimEngine.Contracts;

namespace SimEngine.Server.Grains;

/// <summary>
/// Orleans grain representing a single player. Buffers the player's commands
/// and tracks which session the player has joined; flushing forwards the
/// buffered commands to the <see cref="IGameSessionGrain"/>, which applies them
/// at its next step boundary. The grain key is the player ID.
/// </summary>
public sealed class PlayerGrain : Grain, IPlayerGrain
{
    private readonly List<PlayerCommand> _buffer = [];
    private string? _sessionId;

    /// <inheritdoc />
    public async Task JoinSessionAsync(string sessionId, string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        if (_sessionId is not null)
        {
            if (string.Equals(_sessionId, sessionId, StringComparison.Ordinal))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Player is already in session '{_sessionId}'. Leave it before joining another.");
        }

        var session = GrainFactory.GetGrain<IGameSessionGrain>(sessionId);
        await session.JoinAsync(this.GetPrimaryKeyString(), contentHash);
        _sessionId = sessionId;
    }

    /// <inheritdoc />
    public async Task LeaveSessionAsync()
    {
        if (_sessionId is null)
        {
            return;
        }

        var session = GrainFactory.GetGrain<IGameSessionGrain>(_sessionId);
        await session.LeaveAsync(this.GetPrimaryKeyString());
        _sessionId = null;
        _buffer.Clear();
    }

    /// <inheritdoc />
    public Task SubmitCommandAsync(PlayerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        RequireSession();
        _buffer.Add(command);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<int> FlushCommandsAsync()
    {
        var sessionId = RequireSession();
        if (_buffer.Count == 0)
        {
            return 0;
        }

        var commands = _buffer.ToArray();
        var session = GrainFactory.GetGrain<IGameSessionGrain>(sessionId);
        await session.EnqueueCommandsAsync(commands);
        _buffer.Clear();
        return commands.Length;
    }

    /// <inheritdoc />
    public Task<string?> GetCurrentSessionAsync() => Task.FromResult(_sessionId);

    /// <inheritdoc />
    public Task<int> GetBufferedCommandCountAsync() => Task.FromResult(_buffer.Count);

    private string RequireSession() =>
        _sessionId ?? throw new InvalidOperationException(
            "Player has not joined a session. Call JoinSessionAsync first.");
}

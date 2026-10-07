using SimEngine.Contracts;
using SimEngine.Contracts.Wire;

namespace SimEngine.Client;

/// <summary>
/// A live client view of one session: a <see cref="SessionStateCache"/> kept
/// current by the update stream. Updates are applied (and acked) on the
/// connection's read thread.
/// </summary>
public sealed class SessionSubscription : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly List<object> _pending = [];
    private readonly SessionClient _session;
    private SessionStateCache? _cache;
    private string? _endReason;

    private SessionSubscription(SessionClient session)
    {
        _session = session;
    }

    public string SessionId => _session.SessionId;

    /// <summary>The session's synced read model.</summary>
    public SessionStateCache Cache => _cache ?? throw new InvalidOperationException("The subscription has not started.");

    /// <summary>
    /// Registers before subscribing so updates that overtake the snapshot reply
    /// are buffered, then applied on top of the snapshot (older ones are ignored).
    /// </summary>
    internal static async Task<SessionSubscription> StartAsync(SessionClient session, CancellationToken ct)
    {
        var subscription = new SessionSubscription(session);
        session.Client.Register(subscription);
        try
        {
            var reply = await session.Client.Connection.RequestAsync<ServerMessages.SnapshotReply>(
                id => new ClientMessages.Subscribe(id, session.SessionId), GameClient.RequestTimeout, ct).ConfigureAwait(false);
            subscription.Attach(reply.Snapshot);
            return subscription;
        }
        catch
        {
            session.Client.Unregister(subscription);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _session.Client.Unregister(this);
        try
        {
            await _session.Client.Connection.TryRequestAsync(
                id => new ClientMessages.Unsubscribe(id, SessionId), WireErrorCode.NotJoined, GameClient.RequestTimeout, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (ConnectionLostException)
        {
        }
    }

    internal void OnUpdate(SessionStreamUpdate update)
    {
        lock (_gate)
        {
            if (_cache is null)
            {
                _pending.Add(update);
                return;
            }

            _cache.Apply(update);
        }
    }

    internal void OnSnapshot(SessionSnapshot snapshot)
    {
        lock (_gate)
        {
            if (_cache is null)
            {
                _pending.Add(snapshot);
                return;
            }

            if (snapshot.Sequence > _cache.Sequence)
            {
                _cache.ApplySnapshot(snapshot);
            }
        }
    }

    internal void OnEnded(string reason)
    {
        lock (_gate)
        {
            if (_cache is null)
            {
                _endReason = reason;
                return;
            }

            _cache.MarkEnded(reason);
        }
    }

    private void Attach(SessionSnapshot snapshot)
    {
        lock (_gate)
        {
            _cache = new SessionStateCache(snapshot);
            foreach (var message in _pending)
            {
                switch (message)
                {
                    case SessionStreamUpdate update:
                        _cache.Apply(update);
                        break;
                    case SessionSnapshot newer when newer.Sequence > _cache.Sequence:
                        _cache.ApplySnapshot(newer);
                        break;
                }
            }

            _pending.Clear();
            if (_endReason is not null)
            {
                _cache.MarkEnded(_endReason);
            }
        }
    }
}

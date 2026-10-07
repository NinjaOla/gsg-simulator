using Akka.Actor;
using SimEngine.Client;
using SimEngine.Contracts;
using SimEngine.Server.Direct;

namespace SimEngine.Server.Tests;

/// <summary>
/// Akka-level session subscription for actor tests: a subscriber actor folds
/// updates into a <see cref="SessionStateCache"/> and acks each one.
/// </summary>
public sealed class ActorSessionSubscription : IAsyncDisposable
{
    private readonly ActorSessionClient _session;
    private readonly IActorRef _subscriber;

    private ActorSessionSubscription(ActorSessionClient session, IActorRef subscriber, SessionStateCache cache)
    {
        _session = session;
        _subscriber = subscriber;
        Cache = cache;
    }

    public SessionStateCache Cache { get; }

    /// <summary>Subscribes before fetching the snapshot so no update is lost in between.</summary>
    internal static async Task<ActorSessionSubscription> StartAsync(ActorSystem system, ActorSessionClient session, CancellationToken ct)
    {
        var subscriber = system.ActorOf(Props.Create(() => new CacheUpdaterActor(session.SessionId)));
        try
        {
            await session.SubscribeAsync(subscriber, ct);
            var snapshot = await session.GetSnapshotAsync(ct);
            var cache = new SessionStateCache(snapshot);
            subscriber.Tell(new CacheUpdaterActor.Attach(cache));
            return new ActorSessionSubscription(session, subscriber, cache);
        }
        catch
        {
            system.Stop(subscriber);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _session.UnsubscribeAsync(_subscriber);
        }
        finally
        {
            _subscriber.Tell(PoisonPill.Instance);
        }
    }

    private sealed class CacheUpdaterActor : ReceiveActor
    {
        private readonly List<object> _pending = [];
        private SessionStateCache? _cache;

        public CacheUpdaterActor(string sessionId)
        {
            Receive<SessionStreamUpdate>(update =>
            {
                Handle(update);
                Sender.Tell(new SessionProtocol.AckUpdate(sessionId, update.Sequence));
            });
            Receive<SessionSnapshot>(snapshot =>
            {
                Handle(snapshot);
                Sender.Tell(new SessionProtocol.AckUpdate(sessionId, snapshot.Sequence));
            });
            Receive<Attach>(msg =>
            {
                _cache = msg.Cache;
                foreach (var message in _pending)
                {
                    Handle(message);
                }

                _pending.Clear();
            });
        }

        private void Handle(object message)
        {
            if (_cache is null)
            {
                _pending.Add(message);
                return;
            }

            switch (message)
            {
                case SessionStreamUpdate update:
                    _cache.Apply(update);
                    break;
                case SessionSnapshot snapshot when snapshot.Sequence > _cache.Sequence:
                    _cache.ApplySnapshot(snapshot);
                    break;
            }
        }

        public sealed record Attach(SessionStateCache Cache);
    }
}

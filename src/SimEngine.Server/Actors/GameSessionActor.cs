using Akka.Actor;
using Akka.Event;
using SimEngine.Contracts;
using SimEngine.Game;
using SimEngine.Game.Components;
using SimEngine.Game.Events;
using SimEngine.Game.Seeding;
using SimEngine.Server.Worlds;
using SimEngine.State.Components;
using SimEngine.State.Loading;
using SimEngine.State.Loading.GeoJson;

namespace SimEngine.Server.Actors;

/// <summary>
/// Akka actor that owns a <see cref="SimulationEngine"/> instance — the single
/// simulation authority for one game session. World loading, seeding, stepping,
/// and save/load all happen here; clients only hold the actor reference (plus,
/// in-process, a read-only engine view via <see cref="ILocalEngineProvider"/>).
/// Per-tick updates are broadcast to subscribed observer actors instead of an
/// Orleans stream.
/// </summary>
public sealed class GameSessionActor : ReceiveActor
{
    private const string WorldNameMetadataKey = "worldName";
    private const int MaxEventsPerUpdate = 200;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly string _sessionId;
    private readonly ILocalEngineProvider? _engineProvider;
    private readonly Queue<PlayerCommand> _commandQueue = new();
    private readonly SortedSet<string> _players = new(StringComparer.Ordinal);
    private readonly List<string> _pendingStreamEvents = [];
    private readonly Dictionary<string, long> _lastPublishedFunds = new(StringComparer.Ordinal);
    private readonly HashSet<IActorRef> _subscribers = [];

    private SimulationEngine? _engine;
    private string? _contentHash;
    private IDisposable? _engineEventSubscription;
    private bool _paused;

    /// <summary>Creates the props for a session actor with the given entity id.</summary>
    public static Props Props(string sessionId, ILocalEngineProvider? engineProvider) =>
        Akka.Actor.Props.Create(() => new GameSessionActor(sessionId, engineProvider));

    public GameSessionActor(string sessionId, ILocalEngineProvider? engineProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _sessionId = sessionId;
        _engineProvider = engineProvider;

        Receive<SessionProtocol.Initialize>(msg => Reply(() => Initialize(msg)));
        Receive<SessionProtocol.InitializeFromSave>(msg => Reply(() => InitializeFromSave(msg)));
        Receive<SessionProtocol.EnqueueCommand>(msg => Reply(() => EnqueueCommand(msg)));
        Receive<SessionProtocol.EnqueueCommands>(msg => Reply(() => EnqueueCommands(msg)));
        Receive<SessionProtocol.Join>(msg => Reply(() => Join(msg)));
        Receive<SessionProtocol.Leave>(msg => Reply(() => Leave(msg)));
        Receive<SessionProtocol.GetPlayers>(_ => Reply(() => _players.ToArray()));
        Receive<SessionProtocol.GetSnapshot>(_ => Reply(BuildSnapshot));
        Receive<SessionProtocol.Advance>(msg => Reply(() => Advance(msg)));
        Receive<SessionProtocol.Save>(msg => Reply(() => Save(msg)));
        Receive<SessionProtocol.GetTickNumber>(_ => Reply(() => GetEngine().TickNumber));
        Receive<SessionProtocol.GetCurrentDate>(_ => Reply(() => GetEngine().Time.GetUtcNow()));
        Receive<SessionProtocol.Subscribe>(msg => Reply(() => Subscribe(msg)));
        Receive<SessionProtocol.Unsubscribe>(msg => Reply(() => Unsubscribe(msg)));
        Receive<SessionProtocol.Shutdown>(_ => HandleShutdown());
        Receive<Terminated>(t => _subscribers.Remove(t.ActorRef));
    }

    private SessionInfo Initialize(SessionProtocol.Initialize msg)
    {
        EnsureNotInitialized();
        ArgumentException.ThrowIfNullOrWhiteSpace(msg.WorldId);

        var asset = WorldCatalog.Find(msg.WorldId)
            ?? throw new ArgumentException($"Unknown world id '{msg.WorldId}'.", nameof(msg));

        var worldPath = WorldCatalog.ResolvePath(asset);
        var countriesPath = WorldCatalog.ResolveCountriesPath(asset);
        if (!File.Exists(worldPath))
        {
            throw new FileNotFoundException($"World file for '{msg.WorldId}' not found.", worldPath);
        }
        if (!File.Exists(countriesPath))
        {
            throw new FileNotFoundException($"Countries file for '{msg.WorldId}' not found.", countriesPath);
        }

        using var worldStream = new FileStream(worldPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var worldResult = new GeoJsonWorldLoader().Load(worldStream);
        var state = WorldLoaders.LoadIntoState(worldResult);
        GameWorldSeeder.Seed(state, worldResult, countriesPath);
        state.Metadata[WorldNameMetadataKey] = asset.DisplayName;
        state.Metadata[GameManifestMetadata.ScenarioIdKey] = msg.WorldId;

        _contentHash = ContentHasher.ComputeFromFiles(
            [worldPath, countriesPath],
            GameContentDefaults.ContentVersion);

        var definition = GameDefinition.CreateDefault(
            scenarioId: msg.WorldId,
            contentVersion: GameContentDefaults.ContentVersion,
            contentHash: _contentHash);

        _engine = new SimulationEngine(
            new SimulationEngineOptions
            {
                StartDate = msg.StartDate,
                Seed = msg.Seed,
                InitialState = state,
                ComponentCodecs = definition.ComponentCodecs,
                StateSectionCodecs = definition.StateSectionCodecs,
                SaveMetadata = definition.SaveMetadata,
            },
            definition.Systems);

        CompleteInitialization(_engine);
        return BuildSessionInfo();
    }

    private SessionInfo InitializeFromSave(SessionProtocol.InitializeFromSave msg)
    {
        EnsureNotInitialized();
        ArgumentException.ThrowIfNullOrWhiteSpace(msg.SavePath);

        var definition = GameSaves.ReadDefinition(msg.SavePath);
        _engine = SimulationEngine.Load(
            msg.SavePath,
            definition.Systems,
            definition.ComponentCodecs,
            definition.StateSectionCodecs,
            definition.SaveMetadata);
        _contentHash = definition.Manifest.ContentHash;

        CompleteInitialization(_engine);
        return BuildSessionInfo();
    }

    private Ack EnqueueCommand(SessionProtocol.EnqueueCommand msg)
    {
        ArgumentNullException.ThrowIfNull(msg.Command);
        _commandQueue.Enqueue(msg.Command);
        return Ack.Instance;
    }

    private Ack EnqueueCommands(SessionProtocol.EnqueueCommands msg)
    {
        ArgumentNullException.ThrowIfNull(msg.Commands);
        foreach (var command in msg.Commands)
        {
            ArgumentNullException.ThrowIfNull(command);
            _commandQueue.Enqueue(command);
        }

        return Ack.Instance;
    }

    private Ack Join(SessionProtocol.Join msg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(msg.PlayerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(msg.ContentHash);
        ValidateContentCompatibility(msg.ContentHash);
        _players.Add(msg.PlayerId);
        return Ack.Instance;
    }

    private Ack Leave(SessionProtocol.Leave msg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(msg.PlayerId);
        _players.Remove(msg.PlayerId);
        return Ack.Instance;
    }

    private TickResult Advance(SessionProtocol.Advance msg)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(msg.Ticks);
        var engine = GetEngine();

        var ticksToExecute = msg.Ticks;
        while (_commandQueue.TryDequeue(out var command))
        {
            switch (command)
            {
                case StepPlayerCommand stepCmd:
                    ticksToExecute = Math.Max(ticksToExecute, stepCmd.Ticks);
                    break;
                case PausePlayerCommand:
                    _paused = true;
                    break;
                case ResumePlayerCommand:
                    _paused = false;
                    break;
            }
        }

        if (_paused)
        {
            ticksToExecute = 0;
        }

        for (var i = 0; i < ticksToExecute; i++)
        {
            engine.Step();
        }

        var result = new TickResult
        {
            TickNumber = engine.TickNumber,
            CurrentDate = engine.Time.GetUtcNow(),
            TicksExecuted = ticksToExecute,
        };

        if (ticksToExecute > 0)
        {
            PublishUpdate(result);
        }

        return result;
    }

    private string Save(SessionProtocol.Save msg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(msg.SavePath);
        var engine = GetEngine();
        var fullPath = Path.GetFullPath(msg.SavePath);
        engine.Save(fullPath);
        return fullPath;
    }

    private Ack Subscribe(SessionProtocol.Subscribe msg)
    {
        ArgumentNullException.ThrowIfNull(msg.Subscriber);
        if (_subscribers.Add(msg.Subscriber))
        {
            Context.Watch(msg.Subscriber);
        }

        return Ack.Instance;
    }

    private Ack Unsubscribe(SessionProtocol.Unsubscribe msg)
    {
        ArgumentNullException.ThrowIfNull(msg.Subscriber);
        if (_subscribers.Remove(msg.Subscriber))
        {
            Context.Unwatch(msg.Subscriber);
        }

        return Ack.Instance;
    }

    private void HandleShutdown()
    {
        ReleaseEngine();
        Sender.Tell(Ack.Instance);
        Context.Stop(Self);
    }

    protected override void PostStop()
    {
        ReleaseEngine();
        base.PostStop();
    }

    private void EnsureNotInitialized()
    {
        if (_engine is not null)
        {
            throw new InvalidOperationException("Session is already initialized.");
        }
    }

    private SimulationEngine GetEngine()
    {
        return _engine ?? throw new InvalidOperationException(
            "Session has not been initialized. Send an Initialize message first.");
    }

    /// <summary>
    /// Rejects a join whose content hash disagrees with this session's loaded
    /// content. Skipped before initialization: there is no authoritative hash
    /// yet, and membership is independent of engine init.
    /// </summary>
    private void ValidateContentCompatibility(string contentHash)
    {
        if (_contentHash is null)
        {
            return;
        }

        if (!string.Equals(_contentHash, contentHash, StringComparison.Ordinal))
        {
            throw new ContentMismatchException(_contentHash, contentHash);
        }
    }

    private void ReleaseEngine()
    {
        _engineEventSubscription?.Dispose();
        _engineEventSubscription = null;
        _pendingStreamEvents.Clear();
        _lastPublishedFunds.Clear();
        _players.Clear();

        if (_engine is not null)
        {
            _engineProvider?.Unregister(_sessionId);
            _engine = null;
        }

        _contentHash = null;
    }

    /// <summary>
    /// Registers the engine for in-process readers and starts collecting game
    /// events for the per-tick broadcast.
    /// </summary>
    private void CompleteInitialization(SimulationEngine engine)
    {
        _engineProvider?.Register(_sessionId, engine);

        _engineEventSubscription = engine.Events.Subscribe<IncomeCollectedEvent>(e =>
        {
            if (_pendingStreamEvents.Count < MaxEventsPerUpdate)
            {
                _pendingStreamEvents.Add(
                    $"{e.Date:yyyy-MM-dd} {e.CountryTag} collected {FormatE2(e.IncomeE2)} (treasury {FormatE2(e.FundsE2)})");
            }
        });
    }

    private void PublishUpdate(TickResult result)
    {
        if (_subscribers.Count == 0)
        {
            _pendingStreamEvents.Clear();
            return;
        }

        var update = new SessionStreamUpdate
        {
            Tick = result,
            Events = _pendingStreamEvents.ToArray(),
            CountryDeltas = CollectTreasuryDeltas(),
        };
        _pendingStreamEvents.Clear();

        foreach (var subscriber in _subscribers)
        {
            subscriber.Tell(update);
        }
    }

    /// <summary>
    /// Returns the absolute treasury balance for every country whose funds
    /// changed since the last published update (or snapshot), and updates the
    /// baseline. Only changed countries are emitted, keeping the per-tick
    /// message small at continent scale.
    /// </summary>
    private CountryTreasuryDelta[] CollectTreasuryDeltas()
    {
        var engine = GetEngine();
        var deltas = new List<CountryTreasuryDelta>();

        foreach (var (countryId, country) in engine.State.Entities.Query<CountryComponent>())
        {
            var funds = engine.State.Entities.TryGet<TreasuryComponent>(countryId, out var treasury)
                ? treasury.FundsE2
                : 0L;

            if (_lastPublishedFunds.TryGetValue(country.Tag, out var previous) && previous == funds)
            {
                continue;
            }

            _lastPublishedFunds[country.Tag] = funds;
            deltas.Add(new CountryTreasuryDelta { Tag = country.Tag, FundsE2 = funds });
        }

        return [.. deltas];
    }

    private static string FormatE2(long valueE2) =>
        $"{valueE2 / 100}.{Math.Abs(valueE2 % 100):D2}";

    private SessionInfo BuildSessionInfo()
    {
        var engine = GetEngine();
        var worldName = engine.State.Metadata.TryGetValue(WorldNameMetadataKey, out var name)
            && !string.IsNullOrWhiteSpace(name)
            ? name
            : "(unknown world)";

        return new SessionInfo
        {
            WorldName = worldName,
            TickNumber = engine.TickNumber,
            CurrentDate = engine.Time.GetUtcNow(),
            ProvinceCount = engine.State.Entities.CountOf<ProvinceComponent>(),
            AdjacencyEdgeCount = engine.State.Adjacency.EdgeCount,
        };
    }

    /// <summary>
    /// Builds the full baseline read model from current engine state. The
    /// per-tick delta baseline (<see cref="_lastPublishedFunds"/>) tracks what
    /// has been pushed, and deltas carry absolute balances, so a client that
    /// fetches this snapshot and then applies deltas converges regardless of
    /// when it connected.
    /// </summary>
    private SessionSnapshot BuildSnapshot()
    {
        var engine = GetEngine();
        var worldName = engine.State.Metadata.TryGetValue(WorldNameMetadataKey, out var name)
            && !string.IsNullOrWhiteSpace(name)
            ? name
            : "(unknown world)";

        var countries = new List<CountryState>();
        foreach (var (countryId, country) in engine.State.Entities.Query<CountryComponent>())
        {
            var funds = engine.State.Entities.TryGet<TreasuryComponent>(countryId, out var treasury)
                ? treasury.FundsE2
                : 0L;

            countries.Add(new CountryState
            {
                Tag = country.Tag,
                DisplayName = country.DisplayName,
                FundsE2 = funds,
            });
        }

        return new SessionSnapshot
        {
            WorldName = worldName,
            TickNumber = engine.TickNumber,
            CurrentDate = engine.Time.GetUtcNow(),
            ProvinceCount = engine.State.Entities.CountOf<ProvinceComponent>(),
            AdjacencyEdgeCount = engine.State.Adjacency.EdgeCount,
            Countries = [.. countries],
        };
    }

    private void Reply(Func<object> handler)
    {
        object response;
        try
        {
            response = handler();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Warning(ex, "Session {0} rejected a request: {1}", _sessionId, ex.Message);
            Sender.Tell(new Status.Failure(ex));
            return;
        }

        Sender.Tell(response);
    }
}

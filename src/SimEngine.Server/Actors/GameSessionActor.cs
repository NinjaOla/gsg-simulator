using Akka.Actor;
using System.Diagnostics;
using Akka.Event;
using SimEngine.Contracts;
using SimEngine.Game;
using SimEngine.Game.Components;
using SimEngine.Game.Events;
using SimEngine.Game.Seeding;
using SimEngine.Game.Worlds;
using SimEngine.State;
using SimEngine.State.Components;
using SimEngine.State.Loading;
using SimEngine.State.Loading.GeoJson;

namespace SimEngine.Server.Actors;

/// <summary>
/// Akka actor that owns a <see cref="SimulationEngine"/> instance — the single
/// simulation authority for one game session. World loading, seeding, ticking,
/// and save/load all happen here; clients only hold the actor reference and read
/// state from snapshots and per-tick updates broadcast to subscribed observer actors.
/// </summary>
/// <remarks>
/// Lifecycle: <see cref="SessionStage.Lobby"/> → <see cref="SessionStage.Running"/> ⇄
/// <see cref="SessionStage.Paused"/> → <see cref="SessionStage.Ended"/>. While running,
/// the actor ticks itself with one <see cref="Tick"/> message per engine step, so
/// commands are always processed between ticks.
/// </remarks>
public sealed class GameSessionActor : ReceiveActor
{
    private const string WorldNameMetadataKey = "worldName";
    private const int MaxEventsPerUpdate = 200;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly string _sessionId;
    private readonly SessionRuntimeOptions _options;
    private readonly SortedDictionary<string, PlayerInfo> _players = new(StringComparer.Ordinal);
    private readonly List<string> _pendingStreamEvents = [];
    private readonly Dictionary<string, long> _lastPublishedFunds = new(StringComparer.Ordinal);
    private readonly Dictionary<IActorRef, SubscriberState> _subscribers = [];

    private SimulationEngine? _engine;
    private string? _contentHash;
    private IDisposable? _engineEventSubscription;
    private SessionStage _stage = SessionStage.Lobby;
    private GameSettings _settings = new();
    private int _speed;
    private string? _hostPlayerId;
    private long _sequence;
    private long _tickGeneration;
    private long _lastTickStart;
    private ICancelable? _scheduledTick;

    /// <summary>Creates the props for a session actor with the given entity id.</summary>
    public static Props Props(string sessionId) => Props(sessionId, SessionRuntimeOptions.Default);

    /// <summary>Creates the props for a session actor with explicit runtime options.</summary>
    public static Props Props(string sessionId, SessionRuntimeOptions options) =>
        Akka.Actor.Props.Create(() => new GameSessionActor(sessionId, options));

    public GameSessionActor(string sessionId, SessionRuntimeOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(options);
        if (options.SpeedIntervals.Count != GameSettings.MaxSpeed)
        {
            throw new ArgumentException($"Exactly {GameSettings.MaxSpeed} speed intervals are required.", nameof(options));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxUnackedUpdates);
        _sessionId = sessionId;
        _options = options;
        _speed = _settings.InitialSpeed;

        Receive<SessionProtocol.Initialize>(msg => Reply(() => Initialize(msg)));
        Receive<SessionProtocol.InitializeFromSave>(msg => Reply(() => InitializeFromSave(msg)));
        Receive<SessionProtocol.EnqueueCommand>(msg => Reply(() => EnqueueCommand(msg)));
        Receive<SessionProtocol.EnqueueCommands>(msg => Reply(() => EnqueueCommands(msg)));
        Receive<SessionProtocol.Join>(msg => Reply(() => Join(msg)));
        Receive<SessionProtocol.Leave>(msg => Reply(() => Leave(msg)));
        Receive<SessionProtocol.Disconnect>(msg => Reply(() => Disconnect(msg)));
        Receive<SessionProtocol.StartGame>(msg => Reply(() => StartGame(msg)));
        Receive<SessionProtocol.UpdateSettings>(msg => Reply(() => UpdateSettings(msg)));
        Receive<SessionProtocol.DebugStep>(msg => Reply(() => DebugStep(msg)));
        Receive<SessionProtocol.GetStatus>(_ => Reply(BuildStatus));
        Receive<SessionProtocol.GetPlayers>(_ => Reply(() => _players.Keys.ToArray()));
        Receive<SessionProtocol.GetSnapshot>(_ => Reply(BuildSnapshot));
        Receive<SessionProtocol.Save>(msg => Reply(() => Save(msg)));
        Receive<SessionProtocol.GetTickNumber>(_ => Reply(() => GetEngine().TickNumber));
        Receive<SessionProtocol.GetCurrentDate>(_ => Reply(() => GetEngine().Time.GetUtcNow()));
        Receive<SessionProtocol.Subscribe>(msg => Reply(() => Subscribe(msg)));
        Receive<SessionProtocol.Unsubscribe>(msg => Reply(() => Unsubscribe(msg)));
        Receive<SessionProtocol.AckUpdate>(OnAckUpdate);
        Receive<SessionProtocol.Shutdown>(_ => HandleShutdown());
        Receive<Tick>(OnTick);
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
        if (string.Equals(definition.Manifest.ContentHash, GameSaves.LegacyContentHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "This save was made by an older version without content information and is not compatible.");
        }

        _engine = SimulationEngine.Load(
            msg.SavePath,
            definition.Systems,
            definition.ComponentCodecs,
            definition.StateSectionCodecs,
            definition.SaveMetadata);
        _contentHash = definition.Manifest.ContentHash;
        _settings = SessionSaveSettings.Read(_engine.State.Metadata);
        _speed = _settings.InitialSpeed;

        CompleteInitialization(_engine);
        return BuildSessionInfo();
    }

    private Ack EnqueueCommand(SessionProtocol.EnqueueCommand msg)
    {
        ArgumentNullException.ThrowIfNull(msg.Command);
        RequireMember(msg.PlayerId);
        ApplyCommand(msg.PlayerId, msg.Command);
        return Ack.Instance;
    }

    private Ack EnqueueCommands(SessionProtocol.EnqueueCommands msg)
    {
        ArgumentNullException.ThrowIfNull(msg.Commands);
        RequireMember(msg.PlayerId);
        foreach (var command in msg.Commands)
        {
            ArgumentNullException.ThrowIfNull(command);
            ApplyCommand(msg.PlayerId, command);
        }

        return Ack.Instance;
    }

    /// <summary>
    /// Applies a command immediately. The actor never processes a message
    /// mid-step, so this is always a tick boundary.
    /// </summary>
    private void ApplyCommand(string playerId, PlayerCommand command)
    {
        switch (command)
        {
            case PausePlayerCommand:
                RequirePermission(playerId, _settings.PauseControl, "pause the game");
                Pause();
                break;
            case ResumePlayerCommand:
                RequirePermission(playerId, _settings.PauseControl, "resume the game");
                Resume();
                break;
            case SetSpeedPlayerCommand setSpeed:
                RequirePermission(playerId, _settings.SpeedControl, "change the speed");
                SetSpeed(setSpeed.Speed);
                break;
            default:
                throw new CommandRejectedException($"Unsupported command '{command.GetType().Name}'.");
        }
    }

    private void Pause()
    {
        if (_stage == SessionStage.Paused)
        {
            return;
        }

        RequireStage(SessionStage.Running, "The game is not running.");
        _stage = SessionStage.Paused;
        CancelScheduledTick();
        PublishStatus();
    }

    private void Resume()
    {
        if (_stage == SessionStage.Running)
        {
            return;
        }

        RequireStage(SessionStage.Paused, "The game is not paused.");
        BeginRunning();
        PublishStatus();
    }

    private void SetSpeed(int speed)
    {
        if (!GameSettings.IsValidSpeed(speed))
        {
            throw new CommandRejectedException(
                $"Speed must be between {GameSettings.MinSpeed} and {GameSettings.MaxSpeed}.");
        }

        if (_stage is not (SessionStage.Running or SessionStage.Paused))
        {
            throw new CommandRejectedException("The game has not started.");
        }

        if (speed == _speed)
        {
            return;
        }

        _speed = speed;
        if (_stage == SessionStage.Running)
        {
            // Re-time the pending tick against the new interval.
            CancelScheduledTick();
            ScheduleNextTick();
        }

        PublishStatus();
    }

    private Ack Join(SessionProtocol.Join msg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(msg.PlayerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(msg.ContentHash);
        if (_players.ContainsKey(msg.PlayerId))
        {
            return Ack.Instance;
        }

        RequireNotEnded();
        RequireStage(SessionStage.Lobby, "The game has already started.");
        ValidateContentCompatibility(msg.ContentHash);
        var name = string.IsNullOrWhiteSpace(msg.PlayerName) ? msg.PlayerId : msg.PlayerName;
        _players[msg.PlayerId] = new PlayerInfo(msg.PlayerId, name, Connected: true);
        _hostPlayerId ??= msg.PlayerId;
        PublishStatus();
        return Ack.Instance;
    }

    private Ack Leave(SessionProtocol.Leave msg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(msg.PlayerId);
        RemovePlayer(msg.PlayerId, "The host left the game.");
        return Ack.Instance;
    }

    private Ack Disconnect(SessionProtocol.Disconnect msg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(msg.PlayerId);
        if (!_players.TryGetValue(msg.PlayerId, out var player))
        {
            return Ack.Instance;
        }

        if (_stage == SessionStage.Lobby || _hostPlayerId == msg.PlayerId)
        {
            RemovePlayer(msg.PlayerId, "The host disconnected.");
            return Ack.Instance;
        }

        _players[msg.PlayerId] = player with { Connected = false };
        PublishStatus();
        return Ack.Instance;
    }

    private void RemovePlayer(string playerId, string hostLeftReason)
    {
        if (!_players.Remove(playerId))
        {
            return;
        }

        if (_hostPlayerId == playerId)
        {
            EndSession(hostLeftReason);
        }
        else
        {
            PublishStatus();
        }
    }

    /// <summary>
    /// Ends the session but keeps the actor as a tombstone in <see cref="SessionStage.Ended"/>
    /// so the lobby can see the game is over and remove it.
    /// </summary>
    private void EndSession(string reason)
    {
        _stage = SessionStage.Ended;
        CancelScheduledTick();
        NotifyEnded(reason);
        ReleaseEngine();
    }

    private void NotifyEnded(string reason)
    {
        foreach (var subscriber in _subscribers.Keys)
        {
            subscriber.Tell(new SessionProtocol.SessionEnded(_sessionId, reason));
            Context.Unwatch(subscriber);
        }

        _subscribers.Clear();
    }

    private Ack StartGame(SessionProtocol.StartGame msg)
    {
        RequireHost(msg.PlayerId, "start the game");
        GetEngine();
        RequireStage(SessionStage.Lobby, "The game has already started.");

        _speed = _settings.InitialSpeed;
        if (msg.Paused)
        {
            _stage = SessionStage.Paused;
        }
        else
        {
            BeginRunning();
        }

        PublishStatus();
        return Ack.Instance;
    }

    private Ack UpdateSettings(SessionProtocol.UpdateSettings msg)
    {
        ArgumentNullException.ThrowIfNull(msg.Settings);
        RequireHost(msg.PlayerId, "change the settings");
        RequireStage(SessionStage.Lobby, "Settings can only be changed in the lobby.");
        if (!GameSettings.IsValidSpeed(msg.Settings.InitialSpeed))
        {
            throw new CommandRejectedException(
                $"Initial speed must be between {GameSettings.MinSpeed} and {GameSettings.MaxSpeed}.");
        }

        _settings = msg.Settings;
        _speed = _settings.InitialSpeed;
        PublishStatus();
        return Ack.Instance;
    }

    private TickResult DebugStep(SessionProtocol.DebugStep msg)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(msg.Ticks);
        RequireMember(msg.PlayerId);
        if (!_settings.AllowDebugCommands)
        {
            throw new CommandRejectedException("Debug commands are disabled for this game.");
        }

        RequireStage(SessionStage.Paused, "Debug steps are only allowed while the game is paused.");
        var engine = GetEngine();
        for (var i = 0; i < msg.Ticks; i++)
        {
            RunTick(engine);
        }

        return CurrentTick(engine, msg.Ticks);
    }

    private void BeginRunning()
    {
        _stage = SessionStage.Running;
        _lastTickStart = Stopwatch.GetTimestamp();
        ScheduleNextTick();
    }

    private void OnTick(Tick tick)
    {
        if (tick.Generation != _tickGeneration || _stage != SessionStage.Running || _engine is null)
        {
            return;
        }

        _lastTickStart = Stopwatch.GetTimestamp();
        RunTick(_engine);
        ScheduleNextTick();
    }

    /// <summary>
    /// Schedules the next <see cref="Tick"/> at <c>lastTickStart + interval</c>,
    /// or immediately if that time has passed. Each tick is its own message, so
    /// pause/speed messages queued meanwhile are handled first even at max speed.
    /// </summary>
    private void ScheduleNextTick()
    {
        var delay = _options.GetInterval(_speed) - Stopwatch.GetElapsedTime(_lastTickStart);
        var tick = new Tick(_tickGeneration);
        if (delay <= TimeSpan.Zero)
        {
            Self.Tell(tick);
            return;
        }

        _scheduledTick = Context.System.Scheduler.ScheduleTellOnceCancelable(delay, Self, tick, Self);
    }

    private void CancelScheduledTick()
    {
        _scheduledTick?.Cancel();
        _scheduledTick = null;
        // Invalidates ticks already in the mailbox.
        _tickGeneration++;
    }

    private void RunTick(SimulationEngine engine)
    {
        engine.Step();
        PublishUpdate(CurrentTick(engine, ticksExecuted: 1));
    }

    private static TickResult CurrentTick(SimulationEngine engine, int ticksExecuted) => new()
    {
        TickNumber = engine.TickNumber,
        CurrentDate = engine.Time.GetUtcNow(),
        TicksExecuted = ticksExecuted,
    };

    private void RequireMember(string playerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        if (!_players.ContainsKey(playerId))
        {
            throw new CommandRejectedException($"Player '{playerId}' is not a member of this session.");
        }
    }

    private void RequireHost(string playerId, string action)
    {
        RequireMember(playerId);
        if (!string.Equals(_hostPlayerId, playerId, StringComparison.Ordinal))
        {
            throw new CommandRejectedException($"Only the host may {action}.");
        }
    }

    private void RequirePermission(string playerId, ControlAccess access, string action)
    {
        if (access == ControlAccess.HostOnly)
        {
            RequireHost(playerId, action);
        }
    }

    private void RequireStage(SessionStage stage, string reason)
    {
        if (_stage != stage)
        {
            throw new CommandRejectedException(reason);
        }
    }

    private void RequireNotEnded()
    {
        if (_stage == SessionStage.Ended)
        {
            throw new CommandRejectedException("The game has ended.");
        }
    }

    private string Save(SessionProtocol.Save msg)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(msg.SavePath);
        var engine = GetEngine();
        var fullPath = Path.GetFullPath(msg.SavePath);
        SessionSaveSettings.Write(engine.State.Metadata, _settings);
        engine.Save(fullPath);
        return fullPath;
    }

    private Ack Subscribe(SessionProtocol.Subscribe msg)
    {
        ArgumentNullException.ThrowIfNull(msg.Subscriber);
        RequireNotEnded();
        if (!_subscribers.ContainsKey(msg.Subscriber))
        {
            _subscribers[msg.Subscriber] = new SubscriberState { LastSent = _sequence, LastAcked = _sequence };
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

    private void OnAckUpdate(SessionProtocol.AckUpdate msg)
    {
        if (!_subscribers.TryGetValue(Sender, out var state))
        {
            return;
        }

        state.LastAcked = Math.Max(state.LastAcked, msg.Sequence);
        FlushBacklog(Sender, state);
    }

    private void HandleShutdown()
    {
        if (_stage != SessionStage.Ended)
        {
            NotifyEnded("The game was closed.");
        }

        _stage = SessionStage.Ended;
        CancelScheduledTick();
        ReleaseEngine();
        Sender.Tell(Ack.Instance);
        Context.Stop(Self);
    }

    protected override void PostStop()
    {
        CancelScheduledTick();
        ReleaseEngine();
        base.PostStop();
    }

    private void EnsureNotInitialized()
    {
        RequireNotEnded();
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
        _hostPlayerId = null;
        _engine = null;
        _contentHash = null;
    }

    /// <summary>Starts collecting game events for the per-tick broadcast.</summary>
    private void CompleteInitialization(SimulationEngine engine)
    {
        _engineEventSubscription = engine.Events.Subscribe<IncomeCollectedEvent>(e =>
        {
            if (_pendingStreamEvents.Count < MaxEventsPerUpdate)
            {
                _pendingStreamEvents.Add(
                    $"{e.Date:yyyy-MM-dd} {e.CountryTag} collected {FormatE2(e.IncomeE2)} (treasury {FormatE2(e.FundsE2)})");
            }
        });
    }

    /// <summary>Publishes a stage, speed, settings, or membership change without a tick (no-op before initialization).</summary>
    private void PublishStatus()
    {
        if (_engine is not null)
        {
            PublishUpdate(CurrentTick(_engine, ticksExecuted: 0));
        }
    }

    private void PublishUpdate(TickResult result)
    {
        _sequence++;
        if (_subscribers.Count == 0)
        {
            _pendingStreamEvents.Clear();
            return;
        }

        var update = new SessionStreamUpdate
        {
            SessionId = _sessionId,
            Sequence = _sequence,
            Tick = result,
            Stage = _stage,
            Speed = _speed,
            HostPlayerId = _hostPlayerId,
            Settings = _settings,
            Players = [.. _players.Values],
            Events = _pendingStreamEvents.ToArray(),
            CountryDeltas = CollectTreasuryDeltas(),
        };
        _pendingStreamEvents.Clear();

        foreach (var (subscriber, state) in _subscribers)
        {
            Deliver(subscriber, state, update);
        }
    }

    /// <summary>
    /// Sends the update now if the subscriber is keeping up; otherwise holds it
    /// for a merged catch-up (or drops the backlog for a snapshot resync once it
    /// exceeds <see cref="SessionRuntimeOptions.MaxHeldUpdates"/>). Lagging
    /// subscribers never slow down the tick loop or other subscribers.
    /// </summary>
    private void Deliver(IActorRef subscriber, SubscriberState state, SessionStreamUpdate update)
    {
        if (state.NeedsResync)
        {
            return;
        }

        if (state.Held.Count > 0 || state.InFlight >= _options.MaxUnackedUpdates)
        {
            state.Held.Add(update);
            if (state.Held.Count > _options.MaxHeldUpdates)
            {
                state.Held.Clear();
                state.NeedsResync = true;
            }

            return;
        }

        subscriber.Tell(update);
        state.LastSent = update.Sequence;
    }

    private void FlushBacklog(IActorRef subscriber, SubscriberState state)
    {
        if (state.InFlight >= _options.MaxUnackedUpdates)
        {
            return;
        }

        if (state.NeedsResync)
        {
            if (_engine is null)
            {
                return;
            }

            var snapshot = BuildSnapshot();
            subscriber.Tell(snapshot);
            state.LastSent = snapshot.Sequence;
            state.NeedsResync = false;
            return;
        }

        if (state.Held.Count == 0)
        {
            return;
        }

        var merged = Merge(state.Held);
        state.Held.Clear();
        subscriber.Tell(merged);
        state.LastSent = merged.Sequence;
    }

    /// <summary>
    /// Merges held updates into one: latest tick/stage/speed, latest absolute
    /// treasury per country, and events concatenated (keeping the newest).
    /// </summary>
    private static SessionStreamUpdate Merge(List<SessionStreamUpdate> held)
    {
        var last = held[^1];
        var deltas = new Dictionary<string, CountryTreasuryDelta>(StringComparer.Ordinal);
        foreach (var update in held)
        {
            foreach (var delta in update.CountryDeltas)
            {
                deltas[delta.Tag] = delta;
            }
        }

        var events = held.SelectMany(u => u.Events).ToList();
        if (events.Count > MaxEventsPerUpdate)
        {
            events.RemoveRange(0, events.Count - MaxEventsPerUpdate);
        }

        return last with
        {
            Tick = last.Tick with { TicksExecuted = held.Sum(u => u.Tick.TicksExecuted) },
            Events = [.. events],
            CountryDeltas = [.. deltas.Values],
        };
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
            WorldId = GetWorldId(engine),
            ContentHash = _contentHash ?? string.Empty,
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
                ProvinceCount = engine.State.Relationships.GetOutbound(countryId, RelationshipLabel.Owns).Count(),
            });
        }

        return new SessionSnapshot
        {
            SessionId = _sessionId,
            WorldName = worldName,
            WorldId = GetWorldId(engine),
            ContentHash = _contentHash ?? string.Empty,
            TickNumber = engine.TickNumber,
            CurrentDate = engine.Time.GetUtcNow(),
            ProvinceCount = engine.State.Entities.CountOf<ProvinceComponent>(),
            AdjacencyEdgeCount = engine.State.Adjacency.EdgeCount,
            Countries = [.. countries],
            Sequence = _sequence,
            Stage = _stage,
            Speed = _speed,
            HostPlayerId = _hostPlayerId,
            Settings = _settings,
            Players = [.. _players.Values],
        };
    }

    private SessionStatus BuildStatus() => new()
    {
        Stage = _stage,
        Speed = _speed,
        HostPlayerId = _hostPlayerId,
        Settings = _settings,
        Players = [.. _players.Values],
        TickNumber = _engine?.TickNumber ?? 0,
    };

    private static string GetWorldId(SimulationEngine engine) =>
        engine.State.Metadata.TryGetValue(GameManifestMetadata.ScenarioIdKey, out var worldId) ? worldId : string.Empty;


    /// <summary>Self-message driving one engine step; stale generations are ignored.</summary>
    private sealed record Tick(long Generation);

    private sealed class SubscriberState
    {
        public long LastSent { get; set; }
        public long LastAcked { get; set; }
        public List<SessionStreamUpdate> Held { get; } = [];
        public bool NeedsResync { get; set; }
        public long InFlight => LastSent - LastAcked;
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

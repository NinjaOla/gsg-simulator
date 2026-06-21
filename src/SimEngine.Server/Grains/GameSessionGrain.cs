using System.Collections.Concurrent;
using Orleans.Streams;
using SimEngine.Contracts;
using SimEngine.Game;
using SimEngine.Game.Components;
using SimEngine.Game.Events;
using SimEngine.Game.Seeding;
using SimEngine.Server.Worlds;
using SimEngine.State.Components;
using SimEngine.State.Loading;
using SimEngine.State.Loading.GeoJson;

namespace SimEngine.Server.Grains;

/// <summary>
/// Orleans grain that owns a <see cref="SimulationEngine"/> instance — the
/// single simulation authority for one game session. World loading, seeding,
/// stepping, and save/load all happen here; clients only hold the grain
/// reference (plus, in-process, a read-only engine view via
/// <see cref="ILocalEngineProvider"/>).
/// </summary>
public sealed class GameSessionGrain : Grain, IGameSessionGrain
{
    private const string WorldNameMetadataKey = "worldName";

    private const int MaxEventsPerUpdate = 200;

    private readonly ILocalEngineProvider? _engineProvider;
    private readonly ConcurrentQueue<PlayerCommand> _commandQueue = new();
    private readonly SortedSet<string> _players = new(StringComparer.Ordinal);
    private readonly List<string> _pendingStreamEvents = [];
    private readonly Dictionary<string, long> _lastPublishedFunds = new(StringComparer.Ordinal);
    private SimulationEngine? _engine;
    private string? _contentHash;
    private IDisposable? _engineEventSubscription;
    private IAsyncStream<SessionStreamUpdate>? _stream;
    private bool _streamResolved;
    private bool _paused;

    public GameSessionGrain(ILocalEngineProvider? engineProvider = null)
    {
        _engineProvider = engineProvider;
    }

    /// <inheritdoc />
    public Task<SessionInfo> InitializeAsync(string worldId, DateTimeOffset startDate, ulong seed)
    {
        EnsureNotInitialized();
        ArgumentException.ThrowIfNullOrWhiteSpace(worldId);

        var asset = WorldCatalog.Find(worldId)
            ?? throw new ArgumentException($"Unknown world id '{worldId}'.", nameof(worldId));

        var worldPath = WorldCatalog.ResolvePath(asset);
        if (!File.Exists(worldPath))
        {
            throw new FileNotFoundException($"World file for '{worldId}' not found.", worldPath);
        }

        var state = WorldLoaders.LoadIntoState(new GeoJsonWorldLoader(), worldPath);
        GameWorldSeeder.Seed(state);
        state.Metadata[WorldNameMetadataKey] = asset.DisplayName;

        // Hash the raw world file (plus content version/features) so a joining
        // client that loaded the same content computes a matching hash. Stored
        // for the join gate and embedded in save metadata.
        _contentHash = ContentHasher.ComputeFromFile(worldPath, GameContentDefaults.ContentVersion);

        var definition = GameDefinition.CreateDefault(
            scenarioId: worldId,
            contentVersion: GameContentDefaults.ContentVersion,
            contentHash: _contentHash);

        _engine = new SimulationEngine(
            new SimulationEngineOptions
            {
                StartDate = startDate,
                Seed = seed,
                InitialState = state,
                ComponentCodecs = definition.ComponentCodecs,
                StateSectionCodecs = definition.StateSectionCodecs,
                SaveMetadata = definition.SaveMetadata,
            },
            definition.Systems);

        CompleteInitialization(_engine);

        return Task.FromResult(BuildSessionInfo());
    }

    /// <inheritdoc />
    public Task<SessionInfo> InitializeFromSaveAsync(string savePath)
    {
        EnsureNotInitialized();
        ArgumentException.ThrowIfNullOrWhiteSpace(savePath);

        var definition = GameSaves.ReadDefinition(savePath);
        _engine = SimulationEngine.Load(
            savePath,
            definition.Systems,
            definition.ComponentCodecs,
            definition.StateSectionCodecs,
            definition.SaveMetadata);
        _contentHash = definition.Manifest.ContentHash;

        CompleteInitialization(_engine);

        return Task.FromResult(BuildSessionInfo());
    }

    /// <inheritdoc />
    public Task EnqueueCommandAsync(PlayerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commandQueue.Enqueue(command);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task EnqueueCommandsAsync(PlayerCommand[] commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        foreach (var command in commands)
        {
            ArgumentNullException.ThrowIfNull(command);
            _commandQueue.Enqueue(command);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task JoinAsync(string playerId, string contentHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ValidateContentCompatibility(contentHash);
        _players.Add(playerId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task LeaveAsync(string playerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerId);
        _players.Remove(playerId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string[]> GetPlayersAsync()
    {
        return Task.FromResult(_players.ToArray());
    }

    /// <inheritdoc />
    public Task<SessionSnapshot> GetSnapshotAsync()
    {
        return Task.FromResult(BuildSnapshot());
    }

    /// <inheritdoc />
    public async Task<TickResult> StepAsync(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        var engine = GetEngine();

        var ticksToExecute = ticks;
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
            await PublishUpdateAsync(result);
        }

        return result;
    }

    /// <inheritdoc />
    public Task<string> SaveAsync(string savePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(savePath);

        var engine = GetEngine();
        var fullPath = Path.GetFullPath(savePath);
        engine.Save(fullPath);
        return Task.FromResult(fullPath);
    }

    /// <inheritdoc />
    public Task<long> GetTickNumberAsync()
    {
        return Task.FromResult(GetEngine().TickNumber);
    }

    /// <inheritdoc />
    public Task<DateTimeOffset> GetCurrentDateAsync()
    {
        return Task.FromResult(GetEngine().Time.GetUtcNow());
    }

    /// <inheritdoc />
    public Task ShutdownAsync()
    {
        ReleaseEngine();
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        ReleaseEngine();
        return base.OnDeactivateAsync(reason, cancellationToken);
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
            "Session has not been initialized. Call InitializeAsync first.");
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
            _engineProvider?.Unregister(this.GetPrimaryKeyString());
            _engine = null;
        }

        _contentHash = null;
    }

    /// <summary>
    /// Registers the engine for in-process readers and starts collecting game
    /// events for the session stream.
    /// </summary>
    private void CompleteInitialization(SimulationEngine engine)
    {
        _engineProvider?.Register(this.GetPrimaryKeyString(), engine);

        _engineEventSubscription = engine.Events.Subscribe<IncomeCollectedEvent>(e =>
        {
            if (_pendingStreamEvents.Count < MaxEventsPerUpdate)
            {
                _pendingStreamEvents.Add(
                    $"{e.Date:yyyy-MM-dd} {e.CountryTag} collected {FormatE2(e.IncomeE2)} (treasury {FormatE2(e.FundsE2)})");
            }
        });
    }

    private async Task PublishUpdateAsync(TickResult result)
    {
        var stream = ResolveStream();
        if (stream is null)
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

        await stream.OnNextAsync(update);
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

    private IAsyncStream<SessionStreamUpdate>? ResolveStream()
    {
        if (_streamResolved)
        {
            return _stream;
        }

        _streamResolved = true;
        try
        {
            var provider = this.GetStreamProvider(SessionStreams.ProviderName);
            _stream = provider.GetStream<SessionStreamUpdate>(SessionStreams.For(this.GetPrimaryKeyString()));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException)
        {
            // No stream provider configured on this silo (e.g., a bare test
            // cluster). The session still works; clients fall back to polling.
            _stream = null;
        }

        return _stream;
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
    /// Builds the full baseline read model from current engine state. Read-only:
    /// the per-tick delta baseline (<see cref="_lastPublishedFunds"/>) tracks what
    /// has been pushed to the stream, and deltas carry absolute balances, so a
    /// client that fetches this snapshot and then applies deltas converges
    /// regardless of when it connected.
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
}

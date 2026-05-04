using System.Collections.Concurrent;
using SimEngine.Contracts;
using SimEngine.Game;
using SimEngine.State;

namespace SimEngine.Server.Grains;

/// <summary>
/// Orleans grain that wraps a <see cref="SimulationEngine"/> instance.
/// Each activation manages a single game session identified by its grain key.
/// Queued commands are applied before each tick step.
/// </summary>
public sealed class GameSessionGrain : Grain, IGameSessionGrain
{
    private readonly ILocalEngineProvider? _engineProvider;
    private SimulationEngine? _engine;
    private GameDefinition? _definition;
    private readonly ConcurrentQueue<PlayerCommand> _commandQueue = new();

    public GameSessionGrain(ILocalEngineProvider? engineProvider = null)
    {
        _engineProvider = engineProvider;
    }

    /// <inheritdoc />
    public Task InitializeAsync(string worldName, DateTimeOffset startDate, ulong seed)
    {
        if (_engine is not null)
        {
            throw new InvalidOperationException("Session is already initialized.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(worldName);

        _definition = GameDefinition.CreateDefault(
            scenarioId: worldName,
            contentVersion: "dev",
            contentHash: "dev");

        var state = new SimulationState();
        state.Metadata["worldName"] = worldName;

        _engine = new SimulationEngine(
            new SimulationEngineOptions
            {
                StartDate = startDate,
                Seed = seed,
                InitialState = state,
                ComponentCodecs = _definition.ComponentCodecs,
                StateSectionCodecs = _definition.StateSectionCodecs,
                SaveMetadata = _definition.SaveMetadata,
            },
            _definition.Systems);

        _engineProvider?.Register(this.GetPrimaryKeyString(), _engine);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task EnqueueCommandAsync(PlayerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        _commandQueue.Enqueue(command);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TickResult> StepAsync()
    {
        var engine = GetEngine();

        // Apply queued commands before stepping.
        int ticksToExecute = 1;
        while (_commandQueue.TryDequeue(out var command))
        {
            switch (command)
            {
                case StepPlayerCommand stepCmd:
                    ticksToExecute = Math.Max(1, stepCmd.Ticks);
                    break;
                case PausePlayerCommand:
                    ticksToExecute = 0;
                    break;
                case ResumePlayerCommand:
                    // Resume just ensures we execute at least one tick.
                    ticksToExecute = Math.Max(1, ticksToExecute);
                    break;
            }
        }

        int executed = 0;
        for (int i = 0; i < ticksToExecute; i++)
        {
            engine.Step();
            executed++;
        }

        var result = new TickResult
        {
            TickNumber = engine.TickNumber,
            CurrentDate = engine.Time.GetUtcNow(),
            TicksExecuted = executed,
        };

        return Task.FromResult(result);
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

    private SimulationEngine GetEngine()
    {
        return _engine ?? throw new InvalidOperationException(
            "Session has not been initialized. Call InitializeAsync first.");
    }
}

using SimEngine;
using SimEngine.Client;
using SimEngine.Game.Events;
using SimEngine.State.Components;

namespace SimEngine.Game.Ui.Console.Game;

/// <summary>
/// Client-side handle for one game session. All simulation mutations go
/// through <see cref="Session"/> (an Akka session-actor handle);
/// <see cref="Engine"/> is a read-only view of the actor-owned engine,
/// available because the server runs in-process (single-player). It is only
/// safe to read between session calls - never while an advance is in flight.
/// </summary>
public sealed class GameSession : IDisposable
{
    private const int MaxLogEntries = 200;

    private readonly List<string> _eventLog = [];
    private readonly List<IDisposable> _subscriptions = [];

    public SimulationEngine Engine { get; }
    public SessionClient Session { get; }
    public IServiceProvider Services { get; }
    public string WorldName { get; }
    public string SessionId { get; }
    public bool ShouldQuit { get; set; }
    internal GameSession? ReplacementSession { get; private set; }

    public int ProvinceCount => Engine.State.Entities.CountOf<ProvinceComponent>();
    public int AdjacencyEdgeCount => Engine.State.Adjacency.EdgeCount;
    public IReadOnlyList<string> EventLog => _eventLog;

    public GameSession(
        SimulationEngine engine,
        SessionClient session,
        string worldName,
        string sessionId,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(worldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(services);

        Engine = engine;
        Session = session;
        WorldName = worldName;
        SessionId = sessionId;
        Services = services;

        _subscriptions.Add(engine.Events.Subscribe<IncomeCollectedEvent>(e =>
            AddLog(
                $"[dim]{e.Date:yyyy-MM-dd}[/]  {e.CountryTag} collected [bold]{FormatE2(e.IncomeE2)}[/] " +
                $"[dim](treasury {FormatE2(e.FundsE2)})[/]")));
    }

    public void AddLog(string markup)
    {
        _eventLog.Add(markup);
        if (_eventLog.Count > MaxLogEntries)
            _eventLog.RemoveAt(0);
    }

    /// <summary>Requests that the current session be replaced after the loop exits.</summary>
    public void ReplaceWith(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        ReplacementSession = session;
        ShouldQuit = true;
    }

    public void Dispose()
    {
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();

        try
        {
            // Console host is sync throughout; blocking here is safe (no sync context).
            Session.ShutdownAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Best-effort: the server may already be shutting down.
        }
    }

    private static string FormatE2(long valueE2) =>
        $"{valueE2 / 100}.{Math.Abs(valueE2 % 100):D2}";
}

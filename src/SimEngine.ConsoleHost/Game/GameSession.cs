using SimEngine;
using SimEngine.ConsoleHost.Events;
using SimEngine.State.Components;

namespace SimEngine.ConsoleHost.Game;

public sealed class GameSession : IDisposable
{
    private const int MaxLogEntries = 200;

    private readonly List<string> _eventLog = [];
    private readonly List<IDisposable> _subscriptions = [];

    public SimulationEngine Engine { get; }
    public string WorldName { get; }
    public bool ShouldQuit { get; set; }

    public int ProvinceCount => Engine.State.Entities.CountOf<ProvinceComponent>();
    public int AdjacencyEdgeCount => Engine.State.Adjacency.EdgeCount;
    public IReadOnlyList<string> EventLog => _eventLog;

    public GameSession(SimulationEngine engine, string worldName)
    {
        Engine = engine;
        WorldName = worldName;

        _subscriptions.Add(engine.Events.Subscribe<HeartbeatEvent>(e =>
            AddLog($"[dim]{e.Date:yyyy-MM-dd}[/]  Heartbeat — [bold]{e.EntityCount}[/] entities")));
    }

    public void AddLog(string markup)
    {
        _eventLog.Add(markup);
        if (_eventLog.Count > MaxLogEntries)
            _eventLog.RemoveAt(0);
    }

    public void Dispose()
    {
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();
    }
}

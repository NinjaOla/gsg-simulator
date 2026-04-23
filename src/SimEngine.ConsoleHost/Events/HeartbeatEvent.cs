using SimEngine.Events;

namespace SimEngine.ConsoleHost.Events;

public sealed record HeartbeatEvent(DateTimeOffset Date, int EntityCount) : ISimulationEvent;

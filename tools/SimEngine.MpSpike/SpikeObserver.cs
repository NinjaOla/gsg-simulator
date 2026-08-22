using System.Diagnostics;
using Akka.Actor;
using SimEngine.Contracts;

namespace SimEngine.MpSpike;

/// <summary>
/// Observer actor for the spike broadcast path. Records a timestamp for each
/// issued advance and, when the matching <see cref="SessionStreamUpdate"/>
/// arrives, records the elapsed delivery latency. Completes
/// <see cref="_allDelivered"/> once <see cref="_target"/> updates land.
/// </summary>
internal sealed class SpikeObserver : ReceiveActor
{
    private readonly Queue<long> _issued = new();
    private readonly LatencyStats _stats;
    private readonly int _target;
    private readonly TaskCompletionSource _allDelivered;
    private int _delivered;

    public SpikeObserver(LatencyStats stats, int target, TaskCompletionSource allDelivered)
    {
        _stats = stats;
        _target = target;
        _allDelivered = allDelivered;

        Receive<MarkIssued>(m => _issued.Enqueue(m.Timestamp));
        Receive<SessionStreamUpdate>(_ => OnUpdate());
    }

    private void OnUpdate()
    {
        if (!_issued.TryDequeue(out var timestamp))
        {
            return;
        }

        _stats.Record(Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);
        if (++_delivered >= _target)
        {
            _allDelivered.TrySetResult();
        }
    }

    /// <summary>Marks that an advance was just issued, capturing its timestamp.</summary>
    internal sealed record MarkIssued(long Timestamp);
}

using System.Diagnostics;

namespace SimEngine.MpSpike;

/// <summary>Collects timing samples and reports mean / p50 / p99 in milliseconds.</summary>
internal sealed class LatencyStats(string label)
{
    private readonly List<double> _samplesMs = [];

    public string Label { get; } = label;

    /// <summary>Times one action and records the elapsed milliseconds.</summary>
    public async Task<T> MeasureAsync<T>(Func<Task<T>> action)
    {
        var start = Stopwatch.GetTimestamp();
        var result = await action();
        Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return result;
    }

    /// <summary>Times one synchronous action and records the elapsed milliseconds.</summary>
    public void Measure(Action action)
    {
        var start = Stopwatch.GetTimestamp();
        action();
        Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    public void Record(double elapsedMs) => _samplesMs.Add(elapsedMs);

    /// <summary>Mean of all recorded samples in milliseconds (0 if none).</summary>
    public double Mean => _samplesMs.Count == 0 ? 0.0 : _samplesMs.Average();

    public string Report()
    {
        if (_samplesMs.Count == 0)
        {
            return $"{Label,-44} (no samples)";
        }

        var sorted = _samplesMs.OrderBy(x => x).ToArray();
        var mean = sorted.Average();
        return $"{Label,-44} n={sorted.Length,-5} mean={mean,8:F3} ms  p50={Percentile(sorted, 50),8:F3} ms  p99={Percentile(sorted, 99),8:F3} ms";
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 1)
        {
            return sorted[0];
        }

        var rank = percentile / 100.0 * (sorted.Length - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        if (low == high)
        {
            return sorted[low];
        }

        var weight = rank - low;
        return sorted[low] * (1 - weight) + sorted[high] * weight;
    }
}

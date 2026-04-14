namespace SimEngine.Systems;

/// <summary>
/// Thrown when <see cref="SystemDependencyGraph.Build"/> detects a cycle in
/// the read/write conflict graph — typically caused by two systems whose
/// read/write sets mutually depend on each other.
/// </summary>
public sealed class SystemDependencyCycleException : InvalidOperationException
{
    public SystemDependencyCycleException(IReadOnlyList<string> cycle)
        : base($"System dependency cycle detected: {string.Join(" -> ", cycle)}")
    {
        Cycle = cycle;
    }

    /// <summary>
    /// Names of the systems that form the detected cycle, in traversal order.
    /// The first and last entries are the same system (the loop-closing node).
    /// </summary>
    public IReadOnlyList<string> Cycle { get; }
}

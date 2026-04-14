using System.Collections.Immutable;

namespace SimEngine.Systems;

/// <summary>
/// Layered topological sort over a list of systems. Edges are derived from
/// read/write set overlap using producer-consumer semantics:
///
/// <list type="bullet">
///   <item>If A writes a key and B only reads that key, A runs before B
///         (producer before consumer).</item>
///   <item>If both A and B write the same key, the tiebreak goes to the
///         system that is earlier in <c>(Order, Name)</c> sort order.</item>
///   <item>Read/read overlap is not a conflict.</item>
///   <item>If A produces what B consumes <em>and</em> B produces what A
///         consumes, the graph has a real cycle and
///         <see cref="SystemDependencyCycleException"/> is thrown.</item>
/// </list>
///
/// The result is a sequence of batches: batches execute in order, systems
/// within a batch are independent and may run concurrently. Insertion order
/// into the registry does not affect the graph — systems are pre-sorted by
/// <c>(Order, Name)</c> before edge computation.
/// </summary>
public sealed class SystemDependencyGraph
{
    private SystemDependencyGraph(
        IReadOnlyList<IReadOnlyList<ISimulationSystem>> batches,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies)
    {
        Batches = batches;
        Dependencies = dependencies;
    }

    /// <summary>
    /// Ordered list of batches. Batch <c>i</c> must complete before batch
    /// <c>i + 1</c> begins; within a batch, systems are independent.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<ISimulationSystem>> Batches { get; }

    /// <summary>
    /// For each system (keyed by <see cref="ISimulationSystem.Name"/>), the
    /// names of systems it directly depends on (i.e. must run after).
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Dependencies { get; }

    public static SystemDependencyGraph Build(IReadOnlyList<ISimulationSystem> systems)
    {
        ArgumentNullException.ThrowIfNull(systems);

        if (systems.Count == 0)
        {
            return new SystemDependencyGraph(
                Array.Empty<IReadOnlyList<ISimulationSystem>>(),
                new Dictionary<string, IReadOnlyList<string>>());
        }

        // 1. Stable pre-sort so insertion order cannot affect the graph.
        var sorted = systems
            .OrderBy(s => s.Order)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToArray();

        // 2. Enforce unique names.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in sorted)
        {
            if (string.IsNullOrEmpty(s.Name))
            {
                throw new InvalidOperationException(
                    $"System of type {s.GetType().FullName} has null or empty Name.");
            }

            if (!seen.Add(s.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate system name '{s.Name}' in registry.");
            }
        }

        var n = sorted.Length;
        var readSets = new HashSet<StateKey>[n];
        var writeSets = new HashSet<StateKey>[n];
        for (var i = 0; i < n; i++)
        {
            readSets[i] = new HashSet<StateKey>(sorted[i].Reads);
            writeSets[i] = new HashSet<StateKey>(sorted[i].Writes);
        }

        // 3. Build adjacency from conflict pairs. Edge i -> j means i must
        //    run before j. Because i < j in the pre-sorted order, this is
        //    automatically acyclic for pure conflict-driven edges — but we
        //    still run a cycle check as defensive insurance in case future
        //    extensions add non-pair edges.
        var adjacency = new List<int>[n];
        var inDegree = new int[n];
        for (var i = 0; i < n; i++)
        {
            adjacency[i] = new List<int>();
        }

        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                AnalyzePair(
                    readSets[i], writeSets[i],
                    readSets[j], writeSets[j],
                    out var edgeIJ, out var edgeJI);

                if (edgeIJ)
                {
                    adjacency[i].Add(j);
                    inDegree[j]++;
                }

                if (edgeJI)
                {
                    adjacency[j].Add(i);
                    inDegree[i]++;
                }
            }
        }

        // 4. Layered Kahn's algorithm — batch index = longest-path depth.
        var batchIndex = new int[n];
        var remaining = n;
        var currentLayer = new List<int>();
        for (var i = 0; i < n; i++)
        {
            if (inDegree[i] == 0)
            {
                currentLayer.Add(i);
            }
        }

        var batches = new List<List<int>>();
        var layerNumber = 0;
        while (currentLayer.Count > 0)
        {
            // Preserve pre-sorted order within the layer.
            currentLayer.Sort();
            batches.Add(new List<int>(currentLayer));
            var nextLayer = new List<int>();
            foreach (var i in currentLayer)
            {
                batchIndex[i] = layerNumber;
                remaining--;
                foreach (var successor in adjacency[i])
                {
                    inDegree[successor]--;
                    if (inDegree[successor] == 0)
                    {
                        nextLayer.Add(successor);
                    }
                }
            }

            currentLayer = nextLayer;
            layerNumber++;
        }

        if (remaining > 0)
        {
            var cycle = ExtractCycle(sorted, adjacency);
            throw new SystemDependencyCycleException(cycle);
        }

        // 5. Materialize output types.
        var resultBatches = new IReadOnlyList<ISimulationSystem>[batches.Count];
        for (var b = 0; b < batches.Count; b++)
        {
            var indices = batches[b];
            var arr = new ISimulationSystem[indices.Count];
            for (var k = 0; k < indices.Count; k++)
            {
                arr[k] = sorted[indices[k]];
            }

            resultBatches[b] = arr;
        }

        var dependencyMap = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        for (var i = 0; i < n; i++)
        {
            // Dependencies = predecessors, not successors. Invert the edge list.
            var deps = new List<string>();
            for (var p = 0; p < n; p++)
            {
                if (adjacency[p].Contains(i))
                {
                    deps.Add(sorted[p].Name);
                }
            }

            dependencyMap[sorted[i].Name] = deps;
        }

        return new SystemDependencyGraph(resultBatches, dependencyMap);
    }

    /// <summary>
    /// Analyze the ordering relationship between two systems, where
    /// <paramref name="readsA"/>/<paramref name="writesA"/> belong to the
    /// earlier-sorted system A and <paramref name="readsB"/>/<paramref name="writesB"/>
    /// to the later-sorted B. Emits up to two directed edges: if both
    /// <paramref name="edgeAB"/> and <paramref name="edgeBA"/> are set,
    /// the graph has a genuine cycle.
    /// </summary>
    private static void AnalyzePair(
        HashSet<StateKey> readsA,
        HashSet<StateKey> writesA,
        HashSet<StateKey> readsB,
        HashSet<StateKey> writesB,
        out bool edgeAB,
        out bool edgeBA)
    {
        edgeAB = false;
        edgeBA = false;

        // Producer-consumer: A writes key, B reads but does not write it.
        // A must run before B.
        foreach (var key in writesA)
        {
            if (readsB.Contains(key) && !writesB.Contains(key))
            {
                edgeAB = true;
                break;
            }
        }

        // Producer-consumer in the other direction.
        foreach (var key in writesB)
        {
            if (readsA.Contains(key) && !writesA.Contains(key))
            {
                edgeBA = true;
                break;
            }
        }

        // Write/write: both write the same key. Only add the tiebreak edge
        // if there isn't already a semantic edge, to avoid spurious cycles.
        if (!edgeAB && !edgeBA)
        {
            foreach (var key in writesA)
            {
                if (writesB.Contains(key))
                {
                    edgeAB = true; // A is earlier in sort order → tiebreak A-first.
                    break;
                }
            }
        }
    }

    private static ImmutableArray<string> ExtractCycle(
        ISimulationSystem[] sorted,
        List<int>[] adjacency)
    {
        var n = sorted.Length;
        var state = new byte[n]; // 0 = unvisited, 1 = on stack, 2 = done
        var parent = new int[n];
        Array.Fill(parent, -1);

        for (var start = 0; start < n; start++)
        {
            if (state[start] != 0)
            {
                continue;
            }

            var stack = new Stack<(int node, int nextEdgeIndex)>();
            stack.Push((start, 0));
            state[start] = 1;

            while (stack.Count > 0)
            {
                var (node, edgeIdx) = stack.Pop();
                if (edgeIdx < adjacency[node].Count)
                {
                    stack.Push((node, edgeIdx + 1));
                    var next = adjacency[node][edgeIdx];
                    if (state[next] == 1)
                    {
                        // Found a back-edge: next -> ... -> node -> next.
                        var cycle = new List<string> { sorted[next].Name };
                        var cursor = node;
                        while (cursor != next && cursor != -1)
                        {
                            cycle.Add(sorted[cursor].Name);
                            cursor = parent[cursor];
                        }

                        cycle.Add(sorted[next].Name);
                        cycle.Reverse();
                        return cycle.ToImmutableArray();
                    }

                    if (state[next] == 0)
                    {
                        parent[next] = node;
                        state[next] = 1;
                        stack.Push((next, 0));
                    }
                }
                else
                {
                    state[node] = 2;
                }
            }
        }

        // Shouldn't reach here if caller confirmed remaining > 0.
        return ImmutableArray<string>.Empty;
    }
}

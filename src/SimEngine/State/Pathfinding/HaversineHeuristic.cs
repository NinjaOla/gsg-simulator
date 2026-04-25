namespace SimEngine.State.Pathfinding;

/// <summary>
/// Creates the engine-owned geographic lower-bound heuristic used by A* when
/// a movement model can prove a minimum cost per kilometer.
/// </summary>
public static class HaversineHeuristic
{
    /// <summary>
    /// Creates a deterministic province heuristic that converts a conservative
    /// great-circle lower bound into path cost units.
    /// </summary>
    public static ProvinceHeuristic Create(SimulationState state, int minimumCostPerKilometer)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (minimumCostPerKilometer <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumCostPerKilometer), minimumCostPerKilometer, "Minimum cost per kilometer must be greater than zero.");
        }

        var provinces = FixedPointGeo.CreateProvinceLookup(state);

        return (node, goal) =>
        {
            if (!provinces.TryGetValue(node, out var from))
            {
                throw new InvalidOperationException($"Province {node} is missing centroid data.");
            }

            if (!provinces.TryGetValue(goal, out var to))
            {
                throw new InvalidOperationException($"Province {goal} is missing centroid data.");
            }

            var distanceKilometers = FixedPointGeo.EstimateLowerBoundKilometers(from, to);
            return FixedPointGeo.ScaleKilometersToCost(distanceKilometers, minimumCostPerKilometer);
        };
    }
}

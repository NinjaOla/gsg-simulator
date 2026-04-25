using System.Collections.Frozen;
using SimEngine.Ids;
using SimEngine.State.Components;

namespace SimEngine.State.Pathfinding;

internal static class FixedPointGeo
{
    private const int MicrodegreesPerDegree = 1_000_000;
    private const int MaxLongitudeDeltaE6 = 180 * MicrodegreesPerDegree;
    private const int FullLongitudeSpanE6 = 360 * MicrodegreesPerDegree;
    private const long FixedScale = 1_000_000_000;
    private const int DeltaBucketSizeE6 = 1_000;
    private const int EarthRadiusKilometers = 6_371;
    private static readonly long[] SinSquaredByDelta = BuildSinSquaredByDelta();

    public static FrozenDictionary<ProvinceId, ProvinceGeoPoint> CreateProvinceLookup(SimulationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var provinceCount = state.Entities.CountOf<ProvinceComponent>();
        if (provinceCount < state.Adjacency.ProvinceCount)
        {
            throw new InvalidOperationException(
                $"Adjacency references {state.Adjacency.ProvinceCount} provinces but only {provinceCount} province components were found.");
        }

        var points = new Dictionary<ProvinceId, ProvinceGeoPoint>(provinceCount);
        foreach (var (entityId, component) in state.Entities.Query<ProvinceComponent>())
        {
            Validate(component, entityId);
            points[ProvinceId.OfEntity(entityId)] = new ProvinceGeoPoint(
                component.CentroidLatE6,
                component.CentroidLonE6,
                ToFixedCosine(component.CentroidLatE6));
        }

        return points.ToFrozenDictionary();
    }

    public static int EstimateLowerBoundKilometers(ProvinceGeoPoint from, ProvinceGeoPoint to)
    {
        var deltaLatE6 = Math.Abs((long)from.LatitudeE6 - to.LatitudeE6);
        var deltaLonE6 = NormalizeLongitudeDelta(from.LongitudeE6, to.LongitudeE6);

        var haversineLower = GetSinSquaredLowerBound(deltaLatE6);
        var longitudeTerm = GetSinSquaredLowerBound(deltaLonE6);
        var cosProduct = FloorDivide(from.CosLatitudeFixed * to.CosLatitudeFixed, FixedScale);
        var scaledLongitudeTerm = FloorDivide(cosProduct * longitudeTerm, FixedScale);
        var aLower = haversineLower + scaledLongitudeTerm;

        if (aLower < 0)
        {
            throw new InvalidOperationException("Computed a negative haversine lower bound.");
        }

        var sqrtFixed = IntegerSquareRoot(aLower * FixedScale);
        var distanceKilometers = FloorDivide(2L * EarthRadiusKilometers * sqrtFixed, FixedScale);
        return distanceKilometers >= int.MaxValue ? int.MaxValue : (int)distanceKilometers;
    }

    public static int ScaleKilometersToCost(int distanceKilometers, int minimumCostPerKilometer)
    {
        if (distanceKilometers < 0)
        {
            throw new InvalidOperationException("Distance lower bound cannot be negative.");
        }

        var scaled = (long)distanceKilometers * minimumCostPerKilometer;
        return scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
    }

    private static void Validate(ProvinceComponent component, EntityId entityId)
    {
        if (component.CentroidLatE6 is < -90_000_000 or > 90_000_000)
        {
            throw new InvalidOperationException(
                $"Province entity {entityId} has an invalid latitude centroid {component.CentroidLatE6}.");
        }

        if (component.CentroidLonE6 is < -180_000_000 or > 180_000_000)
        {
            throw new InvalidOperationException(
                $"Province entity {entityId} has an invalid longitude centroid {component.CentroidLonE6}.");
        }
    }

    private static long ToFixedCosine(int latitudeE6)
    {
        var radians = latitudeE6 * (Math.PI / (180d * MicrodegreesPerDegree));
        var cosine = Math.Cos(radians);
        if (cosine < 0)
        {
            return 0;
        }

        return (long)Math.Floor(cosine * FixedScale);
    }

    private static long GetSinSquaredLowerBound(long deltaE6)
    {
        var index = (int)(deltaE6 / DeltaBucketSizeE6);
        return SinSquaredByDelta[index];
    }

    private static long NormalizeLongitudeDelta(int fromLongitudeE6, int toLongitudeE6)
    {
        var delta = Math.Abs((long)fromLongitudeE6 - toLongitudeE6);
        return delta > MaxLongitudeDeltaE6 ? FullLongitudeSpanE6 - delta : delta;
    }

    private static long[] BuildSinSquaredByDelta()
    {
        var entryCount = (MaxLongitudeDeltaE6 / DeltaBucketSizeE6) + 1;
        var values = new long[entryCount];

        for (var i = 0; i < values.Length; i++)
        {
            var deltaDegrees = (double)(i * DeltaBucketSizeE6) / MicrodegreesPerDegree;
            var halfRadians = deltaDegrees * (Math.PI / 360d);
            var sine = Math.Sin(halfRadians);
            values[i] = (long)Math.Floor(sine * sine * FixedScale);
        }

        return values;
    }

    private static long FloorDivide(long dividend, long divisor) => dividend / divisor;

    private static long IntegerSquareRoot(long value)
    {
        if (value < 0)
        {
            throw new InvalidOperationException("Cannot take the square root of a negative value.");
        }

        ulong remainder = (ulong)value;
        ulong result = 0;
        ulong bit = 1UL << 62;

        while (bit > remainder)
        {
            bit >>= 2;
        }

        while (bit != 0)
        {
            if (remainder >= result + bit)
            {
                remainder -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }

            bit >>= 2;
        }

        return (long)result;
    }

    internal readonly record struct ProvinceGeoPoint(
        int LatitudeE6,
        int LongitudeE6,
        long CosLatitudeFixed);
}

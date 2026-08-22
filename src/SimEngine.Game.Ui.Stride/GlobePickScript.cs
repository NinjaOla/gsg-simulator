using Stride.CommunityToolkit.Engine;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;

namespace SimEngine.Game.Ui.Stride;

internal sealed class GlobePickScript : SyncScript
{
    public required CameraComponent Camera { get; init; }
    public required GeoJsonProvinceIndex ProvinceIndex { get; init; }
    public float GlobeRadius { get; init; }

    public override void Update()
    {
        if (Input is null || !Input.IsMouseButtonPressed(MouseButton.Left))
        {
            return;
        }

        var ray = Camera.GetPickRay(Input.MousePosition);
        if (!TryIntersectSphere(ray, Vector3.Zero, GlobeRadius, out var hitPoint))
        {
            return;
        }

        var (lon, lat) = GeoProjection.ToLonLat(hitPoint);
        var result = ProvinceIndex.Lookup(lon, lat);

        var mode = result.IsInside ? "inside" : "nearest";
        var message =
            $"Pick lon/lat=({lon:F4},{lat:F4}) province={result.Name} [{result.Id}] mode={mode} borderDistDeg={result.BorderDistanceDegrees:F4}";

        Console.WriteLine(message);
    }

    private static bool TryIntersectSphere(Ray ray, Vector3 center, float radius, out Vector3 hitPoint)
    {
        var m = ray.Position - center;
        var b = Vector3.Dot(m, ray.Direction);
        var c = Vector3.Dot(m, m) - radius * radius;

        if (c > 0f && b > 0f)
        {
            hitPoint = Vector3.Zero;
            return false;
        }

        var discriminant = b * b - c;
        if (discriminant < 0f)
        {
            hitPoint = Vector3.Zero;
            return false;
        }

        var t = -b - MathF.Sqrt(discriminant);
        if (t < 0f)
        {
            t = 0f;
        }

        hitPoint = ray.Position + ray.Direction * t;
        return true;
    }
}



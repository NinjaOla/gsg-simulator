using Stride.Core.Mathematics;

namespace SimEngine.Game.Ui.Stride;

internal static class GeoProjection
{
    private const float MaxMercatorLatitude = 85.05112878f;

    public static Vector3 ToUnitSphere(float longitudeDegrees, float latitudeDegrees)
    {
        var lon = MathUtil.DegreesToRadians(longitudeDegrees);
        var lat = MathUtil.DegreesToRadians(latitudeDegrees);

        var cosLat = MathF.Cos(lat);

        return new Vector3(
            x: MathF.Sin(lon) * cosLat,
            y: MathF.Sin(lat),
            z: MathF.Cos(lon) * cosLat);
    }

    public static Vector2 ToMercatorUv(float longitudeDegrees, float latitudeDegrees)
    {
        var lat = MathUtil.Clamp(latitudeDegrees, -MaxMercatorLatitude, MaxMercatorLatitude);

        var u = (180f + longitudeDegrees) / 360f;
        var v = (180f - (180f / MathF.PI * MathF.Log(MathF.Tan(MathF.PI / 4f + lat * MathF.PI / 360f)))) / 360f;

        return new Vector2(u, v);
    }

    public static Vector3 MercatorUvToUnitSphere(float mercatorU, float mercatorV)
    {
        var sphericalX = Mod(mercatorU * MathF.PI * 2f + MathF.PI, MathF.PI * 2f);
        var sphericalY = 2f * MathF.Atan(MathF.Exp(MathF.PI - (mercatorV * MathF.PI * 2f))) - MathF.PI * 0.5f;

        var len = MathF.Cos(sphericalY);

        return new Vector3(
            x: MathF.Sin(sphericalX) * len,
            y: MathF.Sin(sphericalY),
            z: MathF.Cos(sphericalX) * len);
    }

    public static (float Lon, float Lat) ToLonLat(Vector3 surfacePoint)
    {
        var unit = Vector3.Normalize(surfacePoint);

        var latRadians = MathF.Asin(MathUtil.Clamp(unit.Y, -1f, 1f));
        var lonRadians = MathF.Atan2(unit.X, unit.Z);

        var lon = MathUtil.RadiansToDegrees(lonRadians);
        var lat = MathUtil.RadiansToDegrees(latRadians);

        return (lon, lat);
    }

    private static float Mod(float value, float modulus)
    {
        var remainder = value % modulus;
        return remainder < 0f ? remainder + modulus : remainder;
    }
}



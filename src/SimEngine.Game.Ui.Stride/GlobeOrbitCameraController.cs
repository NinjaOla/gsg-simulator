using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;

namespace SimEngine.Game.Ui.Stride;

internal sealed class GlobeOrbitCameraController : SyncScript
{
    public Vector3 Target { get; init; } = Vector3.Zero;
    public float Radius { get; set; } = 9f;
    public float MinRadius { get; init; } = 3f;
    public float MaxRadius { get; init; } = 24f;
    public float OrbitSensitivity { get; init; } = 0.01f;
    public float ZoomSensitivity { get; init; } = 0.9f;

    private float yaw = MathUtil.PiOverFour;
    private float pitch = -0.32f;

    public override void Update()
    {
        if (Input is null)
        {
            return;
        }

        if (Input.IsMouseButtonDown(MouseButton.Right))
        {
            var delta = Input.MouseDelta;
            yaw -= delta.X * OrbitSensitivity;
            pitch += delta.Y * OrbitSensitivity;
            pitch = MathUtil.Clamp(pitch, -1.45f, 1.45f);
        }

        var wheel = Input.MouseWheelDelta;
        if (Math.Abs(wheel) > float.Epsilon)
        {
            Radius = MathUtil.Clamp(Radius - wheel * ZoomSensitivity, MinRadius, MaxRadius);
        }

        var cosPitch = MathF.Cos(pitch);
        var offset = new Vector3(
            Radius * MathF.Sin(yaw) * cosPitch,
            Radius * MathF.Sin(pitch),
            Radius * MathF.Cos(yaw) * cosPitch);

        var eye = Target + offset;
        Entity.Transform.Position = eye;

        var view = Matrix.LookAtLH(eye, Target, Vector3.UnitY);
        var world = Matrix.Invert(view);
        Entity.Transform.Rotation = Quaternion.RotationMatrix(world);
    }
}



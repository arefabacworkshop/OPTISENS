namespace OPTISENS.App.Rendering;

public static class Camera
{
    public static Mat4 BuildViewProjection(double yawDeg, double pitchDeg, int width, int height)
    {
        float aspect = Math.Max(0.1f, width / (float)Math.Max(1, height));
        // 103 deg horizontal-equivalent FOV -> fovY = 2 * atan(tan(51.5 deg) * h/w)
        float fovY = 2.0f * MathF.Atan(MathF.Tan(51.5f * MathF.PI / 180.0f) / aspect);
        var proj = Mat4.Perspective(fovY, aspect, 0.1f, 100.0f);

        float pitchRad = (float)(pitchDeg * Math.PI / 180.0);
        float yawRad = (float)(yawDeg * Math.PI / 180.0);
        var rx = Mat4.RotationX(-pitchRad);
        var ry = Mat4.RotationY(yawRad);
        var view = Mat4.Multiply(rx, ry);
        return Mat4.Multiply(proj, view);
    }
}

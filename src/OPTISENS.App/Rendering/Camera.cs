namespace OPTISENS.App.Rendering;

public static class Camera
{
    public static Mat4 BuildViewProjection(double yawDeg, double pitchDeg, int width, int height, double horizontalFovDeg = 103.0)
    {
        float aspect = Math.Max(0.1f, width / (float)Math.Max(1, height));
        float halfHorizRad = (float)(Math.Clamp(horizontalFovDeg, 70.0, 130.0) * 0.5 * Math.PI / 180.0);
        float fovY = 2.0f * MathF.Atan(MathF.Tan(halfHorizRad) / aspect);
        var proj = Mat4.Perspective(fovY, aspect, 0.1f, 100.0f);

        float pitchRad = (float)(pitchDeg * Math.PI / 180.0);
        float yawRad = (float)(yawDeg * Math.PI / 180.0);
        var rx = Mat4.RotationX(-pitchRad);
        var ry = Mat4.RotationY(yawRad);
        var view = Mat4.Multiply(rx, ry);
        return Mat4.Multiply(proj, view);
    }
}

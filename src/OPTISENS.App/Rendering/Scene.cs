using OPTISENS.Core.Config;
using OPTISENS.Core.Kinematics;
using OPTISENS.Core.Session;
using Silk.NET.OpenGL;

namespace OPTISENS.App.Rendering;

public sealed unsafe class Scene : IDisposable
{
    private readonly GL _gl;
    private uint _sphereVao, _sphereVbo, _sphereEbo;
    private int _sphereIndexCount;

    private uint _gridVao, _gridVbo;
    private int _gridVertexCount;

    private uint _tetherVao, _tetherVbo;
    private uint _rippleVao, _rippleVbo;

    private uint _targetProg, _gridProg;
    private int _uTargetMvp, _uTargetColor;
    private int _uGridMvp, _uGridColor;

    public Scene(GL gl)
    {
        _gl = gl;
        _targetProg = ShaderLib.CompileProgram(_gl, ShaderLib.TargetVert, ShaderLib.TargetFrag);
        _uTargetMvp = _gl.GetUniformLocation(_targetProg, "uMvp");
        _uTargetColor = _gl.GetUniformLocation(_targetProg, "uColor");

        _gridProg = ShaderLib.CompileProgram(_gl, ShaderLib.GridVert, ShaderLib.GridFrag);
        _uGridMvp = _gl.GetUniformLocation(_gridProg, "uMvp");
        _uGridColor = _gl.GetUniformLocation(_gridProg, "uColor");

        CreateSphere();
        CreateArenaGrid();
        CreateTetherBuffer();
        _rippleVao = _gl.GenVertexArray();
        _gl.BindVertexArray(_rippleVao);
        _rippleVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _rippleVbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(24 * 3 * sizeof(float)), null, BufferUsageARB.DynamicDraw);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        _gl.BindVertexArray(0);
    }

    private void CreateSphere()
    {
        var (vertices, indices) = SphereMesh.Generate();
        _sphereIndexCount = indices.Length;

        _sphereVao = _gl.GenVertexArray();
        _gl.BindVertexArray(_sphereVao);

        _sphereVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _sphereVbo);
        fixed (float* vp = vertices)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(vertices.Length * sizeof(float)), vp, BufferUsageARB.StaticDraw);

        _sphereEbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _sphereEbo);
        fixed (uint* ip = indices)
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, (nuint)(indices.Length * sizeof(uint)), ip, BufferUsageARB.StaticDraw);

        const uint stride = 6 * sizeof(float);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, (void*)(3 * sizeof(float)));

        _gl.BindVertexArray(0);
    }

    private void CreateArenaGrid()
    {
        const int half = 20;
        const float floorY = -2.2f;
        const float ceilY = 3.2f;
        const float wallR = 9.0f;
        var lines = new List<float>(2048);

        // 1) Floor & ceiling perspective grid
        for (int i = -half; i <= half; i += 2)
        {
            lines.Add(i); lines.Add(floorY); lines.Add(-half);
            lines.Add(i); lines.Add(floorY); lines.Add(half);
            lines.Add(-half); lines.Add(floorY); lines.Add(i);
            lines.Add(half);  lines.Add(floorY); lines.Add(i);
        }

        // 2) Cylindrical 360-degree arena ribs (every 15 degrees) for peripheral optical flow
        const int segs = 72;
        for (int s = 0; s < segs; s++)
        {
            double a0 = 2.0 * Math.PI * s / segs;
            double a1 = 2.0 * Math.PI * (s + 1) / segs;
            float x0 = (float)(wallR * Math.Sin(a0)), z0 = (float)(-wallR * Math.Cos(a0));
            float x1 = (float)(wallR * Math.Sin(a1)), z1 = (float)(-wallR * Math.Cos(a1));

            foreach (float ringY in new[] { -1.5f, 0.0f, 1.5f })
            {
                lines.Add(x0); lines.Add(ringY); lines.Add(z0);
                lines.Add(x1); lines.Add(ringY); lines.Add(z1);
            }

            if (s % 3 == 0) // Every 15 degrees vertical pillar
            {
                lines.Add(x0); lines.Add(floorY); lines.Add(z0);
                lines.Add(x0); lines.Add(ceilY);  lines.Add(z0);
            }
        }

        var verts = lines.ToArray();
        _gridVertexCount = verts.Length / 3;

        _gridVao = _gl.GenVertexArray();
        _gl.BindVertexArray(_gridVao);
        _gridVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _gridVbo);
        fixed (float* vp = verts)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(verts.Length * sizeof(float)), vp, BufferUsageARB.StaticDraw);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        _gl.BindVertexArray(0);
    }

    private void CreateTetherBuffer()
    {
        _tetherVao = _gl.GenVertexArray();
        _gl.BindVertexArray(_tetherVao);
        _tetherVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _tetherVbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(6 * sizeof(float)), null, BufferUsageARB.DynamicDraw);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), (void*)0);
        _gl.BindVertexArray(0);
    }

    private static (float X, float Y, float Z) SphericalToWorld(double azDeg, double elDeg, float distM)
    {
        float az = (float)(azDeg * Math.PI / 180.0);
        float el = (float)(elDeg * Math.PI / 180.0);
        return (
            distM * MathF.Cos(el) * MathF.Sin(az),
            distM * MathF.Sin(el),
            -distM * MathF.Cos(el) * MathF.Cos(az));
    }

    public void Draw(ViewportState state, int width, int height)
    {
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.ClearColor(0.0353f, 0.0392f, 0.0510f, 1.0f); // #090A0D
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var vp = Camera.BuildViewProjection(state.YawDeg, state.PitchDeg, width, height, state.HorizontalFovDeg);

        // 1) Arena cage & floor grid (coral tint on miss so it registers in peripheral vision)
        _gl.UseProgram(_gridProg);
        _gl.UniformMatrix4(_uGridMvp, 1, false, vp.M);
        if (state.MissFlash)
        {
            _gl.Uniform4(_uGridColor, 0.95f, 0.28f, 0.28f, 0.32f);
        }
        else
        {
            _gl.Uniform4(_uGridColor,
                0.10f + state.AccentR * 0.12f,
                0.16f + state.AccentG * 0.12f,
                0.24f + state.AccentB * 0.12f,
                0.22f);
        }
        _gl.BindVertexArray(_gridVao);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gridVertexCount);

        const float distM = 8.0f;

        // 2) Draw 3D guiding tether line for Flick & Dot blocks
        if (state.ActiveBlock is BlockKind.Flick or BlockKind.Dot && state.Targets.Length > 0 && state.Targets[0].HasTether)
        {
            var t0 = state.Targets[0];
            var (px, py, pz) = SphericalToWorld(t0.PrevAzimuthDeg, t0.PrevElevationDeg, distM);
            var (cx, cy, cz) = SphericalToWorld(t0.AzimuthDeg, t0.ElevationDeg, distM);
            float* linePts = stackalloc float[6] { px, py, pz, cx, cy, cz };

            _gl.BindVertexArray(_tetherVao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _tetherVbo);
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(6 * sizeof(float)), linePts);
            _gl.Uniform4(_uGridColor,
                0.10f + state.AccentR * 0.90f,
                0.30f + state.AccentG * 0.64f,
                0.45f + state.AccentB * 0.55f,
                0.55f);
            _gl.DrawArrays(PrimitiveType.Lines, 0, 2);
        }

        // 3) Target spheres at radius = 8m with spawn pop-in & urgency color
        _gl.UseProgram(_targetProg);
        _gl.BindVertexArray(_sphereVao);
        foreach (var target in state.Targets)
        {
            var (tx, ty, tz) = SphericalToWorld(target.AzimuthDeg, target.ElevationDeg, distM);
            float popScale = target.SpawnAgeS < 0.07 ? (float)Math.Clamp(0.35 + 0.65 * (target.SpawnAgeS / 0.07), 0.35, 1.0) : 1.0f;
            float worldR = distM * MathF.Sin((float)(target.RadiusDeg * Math.PI / 180.0)) * popScale;

            var model = Mat4.Multiply(Mat4.Translation(tx, ty, tz), Mat4.Scale(worldR));
            var mvp = Mat4.Multiply(vp, model);
            _gl.UniformMatrix4(_uTargetMvp, 1, false, mvp.M);

            if (target.IsHit || state.HitFlash)
            {
                _gl.Uniform3(_uTargetColor, 0.18f, 0.92f, 0.52f); // Emerald hit/lock
            }
            else if (state.ActiveBlock is BlockKind.Flick or BlockKind.Dot && target.LifeRemaining01 < 0.32)
            {
                _gl.Uniform3(_uTargetColor, 1.0f, 0.42f, 0.28f);  // Coral urgency warning
            }
            else if (state.ActiveBlock == BlockKind.Dot)
            {
                _gl.Uniform3(_uTargetColor, 1.0f, 0.72f, 0.22f);  // Amber precision dot
            }
            else
            {
                _gl.Uniform3(_uTargetColor,
                    0.12f + state.AccentR * 0.88f,
                    0.35f + state.AccentG * 0.60f,
                    0.55f + state.AccentB * 0.45f);
            }

            _gl.DrawElements(PrimitiveType.Triangles, (uint)_sphereIndexCount, DrawElementsType.UnsignedInt, null);
        }

        // 4) Target lifetime ring (Flick & Dot): open arc shrinks with LifeRemaining01
        if (state.ActiveBlock is BlockKind.Flick or BlockKind.Dot)
        {
            _gl.UseProgram(_gridProg);
            _gl.UniformMatrix4(_uGridMvp, 1, false, vp.M);
            _gl.BindVertexArray(_rippleVao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _rippleVbo);
            float* arc = stackalloc float[24 * 3];
            foreach (var target in state.Targets)
            {
                int segs = Math.Clamp((int)Math.Ceiling(24 * target.LifeRemaining01), 2, 24);
                double spanRad = 2.0 * Math.PI * target.LifeRemaining01;
                double ringR = target.RadiusDeg * 1.35;
                for (int i = 0; i < segs; i++)
                {
                    double a = spanRad * i / (segs - 1);
                    var (x, y, z) = SphericalToWorld(
                        target.AzimuthDeg + Math.Cos(a) * ringR,
                        target.ElevationDeg + Math.Sin(a) * ringR,
                        distM);
                    arc[i * 3] = x;
                    arc[i * 3 + 1] = y;
                    arc[i * 3 + 2] = z;
                }
                _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(segs * 3 * sizeof(float)), arc);
                if (target.LifeRemaining01 < 0.32)
                    _gl.Uniform4(_uGridColor, 1.0f, 0.42f, 0.28f, 0.85f);          // coral urgency
                else
                    _gl.Uniform4(_uGridColor,
                        0.10f + state.AccentR * 0.80f,
                        0.28f + state.AccentG * 0.62f,
                        0.42f + state.AccentB * 0.58f,
                        0.55f);                                                    // subtle round accent
                _gl.DrawArrays(PrimitiveType.LineStrip, 0, (uint)segs);
            }
        }

        // 5) Expanding emerald hit-ripple ring on the target sphere
        if (state.HitRippleAgeS < 0.24)
        {
            double progress = Math.Clamp(state.HitRippleAgeS / 0.24, 0.0, 1.0);
            double radiusDeg = Constants.FlickTargetRadiusDeg * (1.0 + 1.4 * progress);
            float* ring = stackalloc float[24 * 3];
            for (int i = 0; i < 24; i++)
            {
                double a = 2.0 * Math.PI * i / 24.0;
                var (x, y, z) = SphericalToWorld(
                    state.LastHitAzimuthDeg + Math.Cos(a) * radiusDeg,
                    state.LastHitElevationDeg + Math.Sin(a) * radiusDeg,
                    distM);
                ring[i * 3] = x;
                ring[i * 3 + 1] = y;
                ring[i * 3 + 2] = z;
            }

            _gl.UseProgram(_gridProg);
            _gl.UniformMatrix4(_uGridMvp, 1, false, vp.M);
            _gl.Uniform4(_uGridColor, 0.18f, 0.92f, 0.52f, (float)(0.85 * (1.0 - progress)));
            _gl.BindVertexArray(_rippleVao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _rippleVbo);
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(24 * 3 * sizeof(float)), ring);
            _gl.DrawArrays(PrimitiveType.LineLoop, 0, 24);
        }

        _gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        _gl.DeleteBuffer(_sphereVbo);
        _gl.DeleteBuffer(_sphereEbo);
        _gl.DeleteVertexArray(_sphereVao);
        _gl.DeleteBuffer(_gridVbo);
        _gl.DeleteVertexArray(_gridVao);
        _gl.DeleteBuffer(_tetherVbo);
        _gl.DeleteVertexArray(_tetherVao);
        _gl.DeleteBuffer(_rippleVbo);
        _gl.DeleteVertexArray(_rippleVao);
        _gl.DeleteProgram(_targetProg);
        _gl.DeleteProgram(_gridProg);
    }
}

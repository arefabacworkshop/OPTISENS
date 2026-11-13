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
        CreateGrid();
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

    private void CreateGrid()
    {
        const int half = 20;
        const float y = -2.0f;
        var lines = new List<float>((half * 2 + 1) * 4 * 3);
        for (int i = -half; i <= half; i++)
        {
            lines.Add(i); lines.Add(y); lines.Add(-half);
            lines.Add(i); lines.Add(y); lines.Add(half);
            lines.Add(-half); lines.Add(y); lines.Add(i);
            lines.Add(half);  lines.Add(y); lines.Add(i);
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

    public void Draw(ViewportState state, int width, int height)
    {
        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.ClearColor(0.0353f, 0.0392f, 0.0510f, 1.0f); // #090A0D
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        var vp = Camera.BuildViewProjection(state.YawDeg, state.PitchDeg, width, height);

        // 1) Grid floor
        _gl.UseProgram(_gridProg);
        _gl.UniformMatrix4(_uGridMvp, 1, false, vp.M);
        _gl.Uniform4(_uGridColor, 0.25f, 0.29f, 0.35f, 0.40f);
        _gl.BindVertexArray(_gridVao);
        _gl.DrawArrays(PrimitiveType.Lines, 0, (uint)_gridVertexCount);

        // 2) Target spheres at radius = 8m
        const float distM = 8.0f;
        _gl.UseProgram(_targetProg);
        _gl.BindVertexArray(_sphereVao);
        foreach (var target in state.Targets)
        {
            float az = (float)(target.AzimuthDeg * Math.PI / 180.0);
            float el = (float)(target.ElevationDeg * Math.PI / 180.0);
            float tx = distM * MathF.Cos(el) * MathF.Sin(az);
            float ty = distM * MathF.Sin(el);
            float tz = -distM * MathF.Cos(el) * MathF.Cos(az);
            float worldR = distM * MathF.Sin((float)(target.RadiusDeg * Math.PI / 180.0));

            var model = Mat4.Multiply(Mat4.Translation(tx, ty, tz), Mat4.Scale(worldR));
            var mvp = Mat4.Multiply(vp, model);
            _gl.UniformMatrix4(_uTargetMvp, 1, false, mvp.M);

            if (target.IsHit || state.HitFlash)
                _gl.Uniform3(_uTargetColor, 0.18f, 0.85f, 0.45f);
            else
                _gl.Uniform3(_uTargetColor, 0.0f, 0.90f, 1.0f);

            _gl.DrawElements(PrimitiveType.Triangles, (uint)_sphereIndexCount, DrawElementsType.UnsignedInt, null);
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
        _gl.DeleteProgram(_targetProg);
        _gl.DeleteProgram(_gridProg);
    }
}

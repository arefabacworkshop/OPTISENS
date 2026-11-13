using Avalonia.Controls;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using OPTISENS.App.Rendering;
using OPTISENS.Core.Session;
using Silk.NET.OpenGL;

namespace OPTISENS.App.Controls;

public sealed class GlViewport : OpenGlControlBase
{
    private GL? _gl;
    private Scene? _scene;

    public event Action<string>? GlInitFailed;
    public event Action<double>? FrameRendered;

    protected override void OnOpenGlInit(GlInterface gl)
    {
        base.OnOpenGlInit(gl);
        try
        {
            _gl = GL.GetApi(gl.GetProcAddress);
            _scene = new Scene(_gl);
        }
        catch (Exception ex)
        {
            GlInitFailed?.Invoke(ex.Message);
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        _scene?.Dispose();
        _scene = null;
        _gl?.Dispose();
        _gl = null;
        base.OnOpenGlDeinit(gl);
    }

    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_gl is null || _scene is null) return;
        double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        int w = Math.Max(1, (int)(Bounds.Width * scale));
        int h = Math.Max(1, (int)(Bounds.Height * scale));
        _gl.Viewport(0, 0, (uint)w, (uint)h);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        _scene.Draw(ViewportState.Snapshot, w, h);
        sw.Stop();
        FrameRendered?.Invoke(sw.Elapsed.TotalMilliseconds);

        Dispatcher.UIThread.Post(RequestNextFrameRendering, DispatcherPriority.Render);
    }
}

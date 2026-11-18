using Silk.NET.OpenGL;

namespace OPTISENS.App.Rendering;

public static class ShaderLib
{
    public const string TargetVert = """
        layout(location=0) in vec3 aPos;
        layout(location=1) in vec3 aNormal;
        uniform mat4 uMvp;
        out vec3 vNormal;
        void main() {
            vNormal = aNormal;
            gl_Position = uMvp * vec4(aPos, 1.0);
        }
        """;

    public const string TargetFrag = """
        in vec3 vNormal;
        out vec4 FragColor;
        uniform vec3 uColor;
        void main() {
            vec3 n = normalize(vNormal);
            vec3 l = normalize(vec3(0.35, 0.78, 0.52));
            vec3 v = vec3(0.0, 0.0, 1.0);
            vec3 h = normalize(l + v);
            float ndl = max(dot(n, l), 0.0);
            float spec = pow(max(dot(n, h), 0.0), 28.0) * 0.45;
            float rim = pow(1.0 - max(abs(n.z), 0.0), 2.4) * 0.55;
            vec3 col = uColor * (0.38 + 0.62 * ndl) + vec3(spec) + uColor * rim;
            FragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
        }
        """;

    public const string GridVert = """
        layout(location=0) in vec3 aPos;
        uniform mat4 uMvp;
        void main() {
            gl_Position = uMvp * vec4(aPos, 1.0);
        }
        """;

    public const string GridFrag = """
        out vec4 FragColor;
        uniform vec4 uColor;
        void main() {
            FragColor = uColor;
        }
        """;

    public static uint CompileProgram(GL gl, string vertSrc, string fragSrc)
    {
        string version = gl.GetStringS(StringName.Version) ?? "";
        string header = version.Contains("OpenGL ES", StringComparison.OrdinalIgnoreCase)
            ? "#version 300 es\nprecision highp float;\n"
            : "#version 330 core\n";

        uint vs = CompileShader(gl, ShaderType.VertexShader, header + vertSrc);
        uint fs = CompileShader(gl, ShaderType.FragmentShader, header + fragSrc);
        uint prog = gl.CreateProgram();
        gl.AttachShader(prog, vs);
        gl.AttachShader(prog, fs);
        gl.LinkProgram(prog);
        gl.GetProgram(prog, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
            throw new InvalidOperationException($"Shader link failed: {gl.GetProgramInfoLog(prog)}");
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        return prog;
    }

    private static uint CompileShader(GL gl, ShaderType type, string src)
    {
        uint s = gl.CreateShader(type);
        gl.ShaderSource(s, src);
        gl.CompileShader(s);
        gl.GetShader(s, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
            throw new InvalidOperationException($"{type} compile failed: {gl.GetShaderInfoLog(s)}");
        return s;
    }
}

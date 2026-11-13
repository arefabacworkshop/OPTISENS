using Silk.NET.OpenGL;

namespace OPTISENS.App.Rendering;

public static class ShaderLib
{
    public const string TargetVert = """
        #version 330 core
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
        #version 330 core
        in vec3 vNormal;
        out vec4 FragColor;
        uniform vec3 uColor;
        void main() {
            float ndl = max(dot(normalize(vNormal), normalize(vec3(0.35, 0.75, 0.55))), 0.0);
            FragColor = vec4(uColor * (0.55 + 0.45 * ndl), 1.0);
        }
        """;

    public const string GridVert = """
        #version 330 core
        layout(location=0) in vec3 aPos;
        uniform mat4 uMvp;
        void main() {
            gl_Position = uMvp * vec4(aPos, 1.0);
        }
        """;

    public const string GridFrag = """
        #version 330 core
        out vec4 FragColor;
        uniform vec4 uColor;
        void main() {
            FragColor = uColor;
        }
        """;

    public static uint CompileProgram(GL gl, string vertSrc, string fragSrc)
    {
        uint vs = CompileShader(gl, ShaderType.VertexShader, vertSrc);
        uint fs = CompileShader(gl, ShaderType.FragmentShader, fragSrc);
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

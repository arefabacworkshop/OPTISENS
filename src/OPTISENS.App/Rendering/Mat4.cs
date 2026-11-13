namespace OPTISENS.App.Rendering;

/// <summary>Column-major 4x4 matrix for OpenGL uniforms (zero-allocation struct).</summary>
public struct Mat4
{
    public unsafe fixed float M[16];

    public static unsafe Mat4 Identity()
    {
        var r = new Mat4();
        r.M[0] = 1; r.M[5] = 1; r.M[10] = 1; r.M[15] = 1;
        return r;
    }

    public static unsafe Mat4 Translation(float x, float y, float z)
    {
        var r = Identity();
        r.M[12] = x; r.M[13] = y; r.M[14] = z;
        return r;
    }

    public static unsafe Mat4 Scale(float s)
    {
        var r = new Mat4();
        r.M[0] = s; r.M[5] = s; r.M[10] = s; r.M[15] = 1;
        return r;
    }

    public static unsafe Mat4 RotationX(float rad)
    {
        var r = Identity();
        float c = MathF.Cos(rad), s = MathF.Sin(rad);
        r.M[5] = c; r.M[6] = s;
        r.M[9] = -s; r.M[10] = c;
        return r;
    }

    public static unsafe Mat4 RotationY(float rad)
    {
        var r = Identity();
        float c = MathF.Cos(rad), s = MathF.Sin(rad);
        r.M[0] = c; r.M[2] = -s;
        r.M[8] = s; r.M[10] = c;
        return r;
    }

    public static unsafe Mat4 Perspective(float fovYRad, float aspect, float near, float far)
    {
        var r = new Mat4();
        float f = 1.0f / MathF.Tan(fovYRad * 0.5f);
        r.M[0] = f / aspect;
        r.M[5] = f;
        r.M[10] = (far + near) / (near - far);
        r.M[11] = -1.0f;
        r.M[14] = (2.0f * far * near) / (near - far);
        return r;
    }

    public static unsafe Mat4 Multiply(in Mat4 a, in Mat4 b)
    {
        var r = new Mat4();
        for (int col = 0; col < 4; col++)
        {
            for (int row = 0; row < 4; row++)
            {
                float sum = 0;
                for (int k = 0; k < 4; k++)
                    sum += a.M[k * 4 + row] * b.M[col * 4 + k];
                r.M[col * 4 + row] = sum;
            }
        }
        return r;
    }
}

namespace OPTISENS.App.Rendering;

/// <summary>Procedural UV-sphere geometry. Generate() runs ONCE at GL init (its array
/// allocations are the only ones in the pipeline); all drawing reuses the uploaded
/// VBO/EBO — zero per-frame allocations.</summary>
public static class SphereMesh
{
    public const int Latitudes = 24;
    public const int Longitudes = 24;

    /// <summary>Interleaved vertices [PX PY PZ NX NY NZ] + CCW triangle indices, unit sphere.
    /// Counts: (24+1)*(24+1) = 625 vertices -> 3750 floats; 24*24*2 triangles -> 3456 indices.</summary>
    public static (float[] Vertices, uint[] Indices) Generate()
    {
        int lat = Latitudes, lon = Longitudes;
        var vertices = new float[(lat + 1) * (lon + 1) * 6];
        int v = 0;
        for (int i = 0; i <= lat; i++)
        {
            double theta = Math.PI * i / lat;
            double st = Math.Sin(theta), ct = Math.Cos(theta);
            for (int j = 0; j <= lon; j++)
            {
                double phi = 2.0 * Math.PI * j / lon;
                float x = (float)(st * Math.Cos(phi));
                float y = (float)ct;
                float z = (float)(st * Math.Sin(phi));
                vertices[v++] = x; vertices[v++] = y; vertices[v++] = z;
                vertices[v++] = x; vertices[v++] = y; vertices[v++] = z;
            }
        }

        var indices = new uint[lat * lon * 6];
        int k = 0, stride = lon + 1;
        for (int i = 0; i < lat; i++)
        {
            for (int j = 0; j < lon; j++)
            {
                uint a = (uint)(i * stride + j);
                uint b = a + (uint)stride;
                indices[k++] = a;     indices[k++] = b;     indices[k++] = a + 1;
                indices[k++] = a + 1; indices[k++] = b;     indices[k++] = b + 1;
            }
        }
        return (vertices, indices);
    }
}

namespace Holograma3D.Core.Domain.Reconstruction;

/// <summary>Valores en un grid 3D: x a la derecha vista de frente, y arriba, z hacia la cámara frontal (z = 0 atrás).</summary>
public sealed class VoxelGrid(int nx, int ny, int nz)
{
    public int Nx { get; } = nx;
    public int Ny { get; } = ny;
    public int Nz { get; } = nz;
    public float[] Values { get; } = new float[nx * ny * nz];

    public float this[int x, int y, int z]
    {
        get => Values[(z * Ny + y) * Nx + x];
        set => Values[(z * Ny + y) * Nx + x] = value;
    }
}

/// <summary>Volumen visual (shape-from-silhouette) a partir de 5 siluetas casi ortográficas.</summary>
public static class VisualHull
{
    public const int MaxResolution = 192;

    /// <summary>Ocupación 0/1 con <paramref name="resolution"/> voxeles en la dimensión mayor.</summary>
    public static VoxelGrid Occupancy(IReadOnlyDictionary<View, Mask> masks, int resolution)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(resolution, 8);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(resolution, MaxResolution);

        var m = Enum.GetValues<View>().ToDictionary(v => v, v => masks[v].LargestComponent());
        // Medidas relativas: cada dimensión sale del promedio de las vistas que la muestran.
        var h = new[] { View.Front, View.Back, View.Left, View.Right }.Average(v => m[v].Height);
        var w = (m[View.Front].Width + m[View.Back].Width) / 2.0;
        var d = (m[View.Left].Width + m[View.Right].Width) / 2.0;
        var scale = resolution / Math.Max(w, Math.Max(h, d));
        int nx = Math.Max(1, (int)Math.Round(w * scale)), ny = Math.Max(1, (int)Math.Round(h * scale)), nz = Math.Max(1, (int)Math.Round(d * scale));

        var front = m[View.Front].Resize(nx, ny);
        var back = m[View.Back].Resize(nx, ny);
        var left = m[View.Left].Resize(nz, ny);
        var right = m[View.Right].Resize(nz, ny);
        var top = m[View.Top].Resize(nx, nz); // fila 0 = la parte trasera

        var grid = new VoxelGrid(nx, ny, nz);
        for (var z = 0; z < nz; z++)
            for (var y = 0; y < ny; y++)
            {
                var row = ny - 1 - y;
                for (var x = 0; x < nx; x++)
                    if (front[x, row] && back[nx - 1 - x, row] && left[z, row] && right[nz - 1 - z, row] && top[x, z])
                        grid[x, y, z] = 1;
            }
        return grid;
    }

    /// <summary>
    /// Rodea el grid con 1 voxel vacío (para que la malla quede cerrada) y lo suaviza con un gaussiano 3D separable (σ = 1):
    /// el campo que recorre Surface Nets con isovalor 0.5.
    /// </summary>
    public static VoxelGrid Smooth(VoxelGrid occupancy)
    {
        var field = new VoxelGrid(occupancy.Nx + 2, occupancy.Ny + 2, occupancy.Nz + 2);
        for (var z = 0; z < occupancy.Nz; z++)
            for (var y = 0; y < occupancy.Ny; y++)
                for (var x = 0; x < occupancy.Nx; x++)
                    field[x + 1, y + 1, z + 1] = occupancy[x, y, z];

        var kernel = Enumerable.Range(-2, 5).Select(i => MathF.Exp(-i * i / 2f)).ToArray();
        var sum = kernel.Sum();
        for (var i = 0; i < kernel.Length; i++)
            kernel[i] /= sum;

        var buffer = new float[field.Values.Length];
        int strideY = field.Nx, strideZ = field.Nx * field.Ny;
        Blur(field.Values, buffer, kernel, 1, field.Nx);
        Blur(buffer, field.Values, kernel, strideY, field.Ny);
        Blur(field.Values, buffer, kernel, strideZ, field.Nz);
        buffer.CopyTo(field.Values, 0);
        return field;
    }

    /// <summary>Convoluciona a lo largo de un eje (dado por su paso en el arreglo y su longitud); fuera del grid vale 0.</summary>
    private static void Blur(float[] src, float[] dst, float[] kernel, int stride, int length)
    {
        var radius = kernel.Length / 2;
        for (var i = 0; i < src.Length; i++)
        {
            var position = i / stride % length;
            float value = 0;
            for (var k = -radius; k <= radius; k++)
            {
                var p = position + k;
                if (p >= 0 && p < length)
                    value += kernel[k + radius] * src[i + k * stride];
            }
            dst[i] = value;
        }
    }
}

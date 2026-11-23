namespace Holograma3D.Core.Domain.Reconstruction;

/// <summary>
/// Las 5 tomas. Izquierda = te desplazas a TU izquierda mirando el frente; superior = desde arriba,
/// con el frente del objeto en el borde inferior de la foto.
/// </summary>
public enum View { Front, Back, Left, Right, Top }

/// <summary>Silueta binaria: true = objeto. Se indexa [columna, fila], con la fila 0 arriba.</summary>
public sealed record Mask(int Width, int Height, bool[] Data)
{
    public bool this[int col, int row] => Data[row * Width + col];

    public int Count => Data.Count(d => d);

    /// <summary>Fracción de píxeles que son objeto.</summary>
    public double Coverage => Data.Length == 0 ? 0 : (double)Count / Data.Length;

    public static Mask From(int width, int height, Func<int, int, bool> isObject)
    {
        var data = new bool[width * height];
        for (var row = 0; row < height; row++)
            for (var col = 0; col < width; col++)
                data[row * width + col] = isObject(col, row);
        return new Mask(width, height, data);
    }

    /// <summary>La componente conectada más grande (4-vecinos), recortada a su bounding box: descarta manchas sueltas.</summary>
    public Mask LargestComponent()
    {
        var label = new int[Data.Length];
        var queue = new Queue<int>();
        int current = 0, best = 0, bestSize = 0;
        (int MinX, int MinY, int MaxX, int MaxY) bestBox = default;

        for (var start = 0; start < Data.Length; start++)
        {
            if (!Data[start] || label[start] != 0)
                continue;
            current++;
            label[start] = current;
            queue.Enqueue(start);
            int size = 0, minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            while (queue.TryDequeue(out var i))
            {
                size++;
                int x = i % Width, y = i / Width;
                (minX, maxX, minY, maxY) = (Math.Min(minX, x), Math.Max(maxX, x), Math.Min(minY, y), Math.Max(maxY, y));
                Visit(x > 0, i - 1);
                Visit(x < Width - 1, i + 1);
                Visit(y > 0, i - Width);
                Visit(y < Height - 1, i + Width);
            }
            if (size > bestSize)
                (best, bestSize, bestBox) = (current, size, (minX, minY, maxX, maxY));
        }
        if (bestSize == 0)
            return this;

        var (bx, by, bw, bh) = (bestBox.MinX, bestBox.MinY, bestBox.MaxX - bestBox.MinX + 1, bestBox.MaxY - bestBox.MinY + 1);
        return From(bw, bh, (col, row) => label[(by + row) * Width + bx + col] == best);

        void Visit(bool inside, int i)
        {
            if (inside && Data[i] && label[i] == 0)
            {
                label[i] = current;
                queue.Enqueue(i);
            }
        }
    }

    /// <summary>Re-muestreo por vecino más cercano.</summary>
    public Mask Resize(int width, int height) =>
        From(width, height, (col, row) => this[
            Math.Min(Width - 1, (int)((col + 0.5) * Width / width)),
            Math.Min(Height - 1, (int)((row + 0.5) * Height / height))]);
}

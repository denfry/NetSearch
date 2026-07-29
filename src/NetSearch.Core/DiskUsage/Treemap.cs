namespace NetSearch.Core.DiskUsage;

public readonly record struct TreemapRect(double X, double Y, double Width, double Height);

/// <summary>
/// Squarified treemap (Bruls, Huizing, van Wijk 2000): packs weighted items into a rectangle so
/// each tile stays close to square. Pure geometry — no UI types — so it is unit-tested directly.
/// </summary>
public static class Treemap
{
    public static IReadOnlyList<TreemapRect> Squarify(
        double boundsWidth, double boundsHeight, IReadOnlyList<double> weights)
    {
        var output = new TreemapRect[weights.Count];
        if (weights.Count == 0) return output;
        if (boundsWidth <= 0 || boundsHeight <= 0) return output; // all zero rects

        // Keep only positive weights, remembering original positions.
        var idx = new List<int>();
        double totalWeight = 0;
        for (int i = 0; i < weights.Count; i++)
            if (weights[i] > 0) { idx.Add(i); totalWeight += weights[i]; }
        if (totalWeight <= 0) return output;

        // Scale weights to areas filling the bounds, sorted largest-first for good aspect ratios.
        idx.Sort((a, b) => weights[b].CompareTo(weights[a]));
        double scale = (boundsWidth * boundsHeight) / totalWeight;
        var areas = idx.Select(i => weights[i] * scale).ToList();

        var rects = new List<TreemapRect>(areas.Count);
        Layout(areas, new TreemapRect(0, 0, boundsWidth, boundsHeight), rects);

        for (int k = 0; k < idx.Count; k++) output[idx[k]] = rects[k];
        return output;
    }

    private static void Layout(List<double> areas, TreemapRect bounds, List<TreemapRect> rects)
    {
        var free = bounds;
        var row = new List<double>();
        double rowSum = 0, rowMax = 0, rowMin = double.MaxValue;

        int i = 0;
        while (i < areas.Count)
        {
            double a = areas[i];
            double side = Math.Min(free.Width, free.Height);
            double newSum = rowSum + a, newMax = Math.Max(rowMax, a), newMin = Math.Min(rowMin, a);

            if (row.Count == 0 ||
                Worst(rowSum, rowMax, rowMin, side) >= Worst(newSum, newMax, newMin, side))
            {
                row.Add(a); rowSum = newSum; rowMax = newMax; rowMin = newMin; i++;
            }
            else
            {
                free = PlaceRow(row, rowSum, free, rects);
                row.Clear(); rowSum = 0; rowMax = 0; rowMin = double.MaxValue;
            }
        }
        if (row.Count > 0) PlaceRow(row, rowSum, free, rects);
    }

    private static double Worst(double sum, double max, double min, double side)
    {
        if (sum <= 0 || side <= 0) return double.MaxValue;
        double s2 = sum * sum, w2 = side * side;
        return Math.Max(w2 * max / s2, s2 / (w2 * min));
    }

    // Lays one row along the shorter side of `free`; returns the remaining free rectangle.
    private static TreemapRect PlaceRow(List<double> row, double rowSum, TreemapRect free, List<TreemapRect> rects)
    {
        double side = Math.Min(free.Width, free.Height);
        double thickness = side <= 0 ? 0 : rowSum / side;

        if (free.Width <= free.Height)
        {
            double x = free.X;
            foreach (var a in row)
            {
                double w = thickness <= 0 ? 0 : a / thickness;
                rects.Add(new TreemapRect(x, free.Y, w, thickness));
                x += w;
            }
            return new TreemapRect(free.X, free.Y + thickness, free.Width, free.Height - thickness);
        }
        else
        {
            double y = free.Y;
            foreach (var a in row)
            {
                double h = thickness <= 0 ? 0 : a / thickness;
                rects.Add(new TreemapRect(free.X, y, thickness, h));
                y += h;
            }
            return new TreemapRect(free.X + thickness, free.Y, free.Width - thickness, free.Height);
        }
    }
}

using System;

namespace ShapesKinect.Core
{
    /// <summary>Traces smooth borders around regions of grid cells.</summary>
    public static class Outline
    {
        /// <summary>
        /// Calls <paramref name="segment"/> with each piece of a smooth line around the cells where
        /// <paramref name="inside"/> is true. Uses marching squares over a 3x3-averaged field, so
        /// edges come out straight and smooth rather than stair-stepped.
        /// </summary>
        public static void Trace(Grid grid, Func<int, bool> inside, Action<float, float, float, float> segment)
        {
            var n = grid.CellsPerSide;
            var smoothed = new float[grid.CellCount];
            for (var row = 0; row < n; row++)
            {
                for (var column = 0; column < n; column++)
                {
                    var count = 0;
                    for (var dr = -1; dr <= 1; dr++)
                    {
                        for (var dc = -1; dc <= 1; dc++)
                        {
                            var r = row + dr;
                            var c = column + dc;
                            if (r >= 0 && r < n && c >= 0 && c < n && inside(r * n + c)) count++;
                        }
                    }
                    smoothed[row * n + column] = count / 9f;
                }
            }

            float At(int column, int row) =>
                column >= 0 && column < n && row >= 0 && row < n ? smoothed[row * n + column] : 0f;
            float Cross(float from, float to) => (0.5f - from) / (to - from);

            var cell = grid.CellSize;
            var offset = grid.Size / 2;
            var xs = new float[4];
            var zs = new float[4];
            var crossing = new int[4];
            for (var row = -1; row < n; row++)
            {
                for (var column = -1; column < n; column++)
                {
                    var va = At(column, row);
                    var vb = At(column + 1, row);
                    var vc = At(column + 1, row + 1);
                    var vd = At(column, row + 1);
                    bool a = va >= 0.5f, b = vb >= 0.5f, c = vc >= 0.5f, d = vd >= 0.5f;
                    if (a == b && b == c && c == d) continue;

                    var x0 = (column + 0.5f) * cell - offset;
                    var z0 = (row + 0.5f) * cell - offset;
                    var x1 = x0 + cell;
                    var z1 = z0 + cell;
                    var count = 0;
                    if (a != b) { xs[0] = x0 + Cross(va, vb) * cell; zs[0] = z0; crossing[count++] = 0; }
                    if (b != c) { xs[1] = x1; zs[1] = z0 + Cross(vb, vc) * cell; crossing[count++] = 1; }
                    if (c != d) { xs[2] = x0 + Cross(vd, vc) * cell; zs[2] = z1; crossing[count++] = 2; }
                    if (d != a) { xs[3] = x0; zs[3] = z0 + Cross(va, vd) * cell; crossing[count++] = 3; }
                    if (count == 2)
                    {
                        segment(xs[crossing[0]], zs[crossing[0]], xs[crossing[1]], zs[crossing[1]]);
                    }
                    else if (a)
                    {
                        // Two diagonal corners inside: cut off each inside corner separately.
                        segment(xs[0], zs[0], xs[3], zs[3]);
                        segment(xs[1], zs[1], xs[2], zs[2]);
                    }
                    else
                    {
                        segment(xs[0], zs[0], xs[1], zs[1]);
                        segment(xs[2], zs[2], xs[3], zs[3]);
                    }
                }
            }
        }
    }
}

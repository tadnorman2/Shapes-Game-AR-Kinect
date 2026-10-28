using System;
using System.Collections.Generic;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>A card seen in the color picture: which one, and where its corners are.</summary>
    public sealed class CardSighting
    {
        public int Id { get; }
        /// <summary>
        /// The corners of the card's black square in full-size color pixels, in printed order: top
        /// left, top right, bottom right, bottom left (the label is at the bottom).
        /// </summary>
        public Vector2[] Corners { get; }

        public CardSighting(int id, Vector2[] corners)
        {
            Id = id;
            Corners = corners;
        }

        /// <summary>The card's area in the picture, in color pixels.</summary>
        public float Area
        {
            get
            {
                var area = 0f;
                for (var i = 0; i < 4; i++)
                {
                    var a = Corners[i];
                    var b = Corners[(i + 1) % 4];
                    area += a.X * b.Y - b.X * a.Y;
                }
                return Math.Abs(area) / 2;
            }
        }
    }

    /// <summary>
    /// Finds the printed cards (see <see cref="CardCodes"/>) in a grey picture: dark blobs are
    /// picked out against their surroundings, those with a four-cornered outline are read cell by
    /// cell, and those reading as a card with an all-black border count. Corners are then set to
    /// a fraction of a pixel along the card's edges.
    /// </summary>
    public sealed class CardDetector
    {
        private int width;
        private int height;
        private int[] integral = new int[0];
        private bool[] dark = new bool[0];
        private bool[] visited = new bool[0];
        private int[] pixels = new int[0];

        /// <summary>Half the side of the square each pixel is compared with, in pixels.</summary>
        private const int Window = 25;
        /// <summary>A pixel counts as dark this much below the brightness around it.</summary>
        private const int DarkerBy = 12;
        /// <summary>Cards smaller than this many pixels across in the grey picture can't be read.</summary>
        private const int MinSide = 10;

        /// <summary>
        /// The cards in <paramref name="image"/>, looking only within <paramref name="area"/>
        /// (left, top, right, bottom, in the grey picture's pixels) if given.
        /// </summary>
        public List<CardSighting> Detect(GrayImage image, (int left, int top, int right, int bottom)? area = null)
        {
            Prepare(image);
            int left = 0, top = 0, right = width, bottom = height;
            if (area != null)
            {
                left = Math.Max(0, area.Value.left);
                top = Math.Max(0, area.Value.top);
                right = Math.Min(width, area.Value.right);
                bottom = Math.Min(height, area.Value.bottom);
            }
            Threshold(image, left, top, right, bottom);

            var found = new List<CardSighting>();
            var maxPixels = (right - left) * (bottom - top) / 3;
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    var start = y * width + x;
                    if (!dark[start] || visited[start]) continue;
                    var count = Fill(start, left, top, right, bottom);
                    if (count < MinSide * 4 || count > maxPixels) continue;
                    var sighting = Read(image, count);
                    if (sighting != null) found.Add(sighting);
                }
            }
            return found;
        }

        private void Prepare(GrayImage image)
        {
            width = image.Width;
            height = image.Height;
            var count = width * height;
            if (dark.Length != count)
            {
                integral = new int[(width + 1) * (height + 1)];
                dark = new bool[count];
                visited = new bool[count];
                pixels = new int[count];
            }
        }

        /// <summary>
        /// Marks pixels in the given part of the picture darker than the average around them, using
        /// a running-sum table built over just that part and the margin around it.
        /// </summary>
        private void Threshold(GrayImage image, int left, int top, int right, int bottom)
        {
            int x0 = Math.Max(0, left - Window), y0 = Math.Max(0, top - Window);
            int x1 = Math.Min(width, right + Window + 1), y1 = Math.Min(height, bottom + Window + 1);
            var stride = x1 - x0 + 1;
            var p = image.Pixels;
            for (var x = 0; x < stride; x++) integral[x] = 0;
            for (var y = y0; y < y1; y++)
            {
                var rowSum = 0;
                var at = (y - y0 + 1) * stride;
                integral[at] = 0;
                for (var x = x0; x < x1; x++)
                {
                    rowSum += p[y * width + x];
                    integral[at + x - x0 + 1] = integral[at - stride + x - x0 + 1] + rowSum;
                }
            }
            for (var y = top; y < bottom; y++)
            {
                int ya = Math.Max(y0, y - Window) - y0, yb = Math.Min(y1, y + Window + 1) - y0;
                for (var x = left; x < right; x++)
                {
                    int xa = Math.Max(x0, x - Window) - x0, xb = Math.Min(x1, x + Window + 1) - x0;
                    var sum = integral[yb * stride + xb] - integral[ya * stride + xb] - integral[yb * stride + xa] + integral[ya * stride + xa];
                    var mean = sum / ((xb - xa) * (yb - ya));
                    var i = y * width + x;
                    dark[i] = p[i] + DarkerBy < mean;
                    visited[i] = false;
                }
            }
        }

        /// <summary>Collects the dark pixels joined to <paramref name="start"/> into <see cref="pixels"/>; returns how many.</summary>
        private int Fill(int start, int left, int top, int right, int bottom)
        {
            var count = 0;
            var head = 0;
            pixels[count++] = start;
            visited[start] = true;
            while (head < count)
            {
                var i = pixels[head++];
                int x = i % width, y = i / width;
                if (x > left) Visit(i - 1);
                if (x < right - 1) Visit(i + 1);
                if (y > top) Visit(i - width);
                if (y < bottom - 1) Visit(i + width);
            }
            return count;

            void Visit(int j)
            {
                if (!dark[j] || visited[j]) return;
                visited[j] = true;
                pixels[count++] = j;
            }
        }

        /// <summary>Reads the blob in <see cref="pixels"/> as a card, if it is one.</summary>
        private CardSighting Read(GrayImage image, int count)
        {
            var quad = Corners(count);
            if (quad == null) return null;
            quad = Refine(image, quad) ?? quad;

            // Each cell's brightness, from a few points around its middle.
            var homography = Homography.FromSquare(quad, CardCodes.Cells);
            var cells = new float[CardCodes.Cells * CardCodes.Cells];
            for (var row = 0; row < CardCodes.Cells; row++)
            {
                for (var column = 0; column < CardCodes.Cells; column++)
                {
                    float sum = 0;
                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var p = homography.Map(column + 0.5f + dx * 0.2f, row + 0.5f + dy * 0.2f);
                            sum += Sample(image, p.X, p.Y);
                        }
                    }
                    cells[row * CardCodes.Cells + column] = sum / 9;
                }
            }
            var sorted = (float[])cells.Clone();
            Array.Sort(sorted);
            float low = sorted[3], high = sorted[sorted.Length - 4];
            if (high - low < 30) return null;
            var threshold = (low + high) / 2;

            var code = 0;
            for (var row = 0; row < CardCodes.Cells; row++)
            {
                for (var column = 0; column < CardCodes.Cells; column++)
                {
                    var black = cells[row * CardCodes.Cells + column] < threshold;
                    var border = row == 0 || column == 0 || row == CardCodes.Cells - 1 || column == CardCodes.Cells - 1;
                    if (border && !black) return null;
                    if (!border && black) code |= 1 << ((row - 1) * 4 + column - 1);
                }
            }
            var match = CardCodes.Match(code);
            if (match == null) return null;

            // Which seen corner each printed corner is: printed corner (u, v) lands at the seen
            // card's grid point given by the same mirror and turn the code was read with.
            var (id, mirrored, turns) = match.Value;
            var printed = new[] { (0f, 0f), (6f, 0f), (6f, 6f), (0f, 6f) };
            var corners = new Vector2[4];
            for (var i = 0; i < 4; i++)
            {
                var (u, v) = CardCodes.TransformPoint(printed[i].Item1, printed[i].Item2, mirrored, turns);
                var seen = u < 3 ? (v < 3 ? 0 : 3) : (v < 3 ? 1 : 2);
                corners[i] = image.ToColor(quad[seen].X, quad[seen].Y);
            }
            return new CardSighting(id, corners);
        }

        /// <summary>
        /// The blob's four outer corners, in order around it (top left of the picture first, then
        /// clockwise as it appears), or null if its outline isn't four-cornered enough.
        /// </summary>
        private Vector2[] Corners(int count)
        {
            Vector2 At(int k) => new Vector2(pixels[k] % width, pixels[k] / width);
            var center = Vector2.Zero;
            for (var k = 0; k < count; k++) center += At(k);
            center /= count;

            var a = Farthest(count, center);
            var b = Farthest(count, a);
            // The two corners farthest to either side of the line from a to b.
            var across = new Vector2(-(b - a).Y, (b - a).X);
            Vector2 c = a, d = a;
            float most = 0, least = 0;
            for (var k = 0; k < count; k++)
            {
                var p = At(k);
                var side = Vector2.Dot(p - a, across);
                if (side > most) { most = side; c = p; }
                if (side < least) { least = side; d = p; }
            }
            var quad = new[] { a, c, b, d };
            for (var i = 0; i < 4; i++)
            {
                if (Vector2.Distance(quad[i], quad[(i + 1) % 4]) < MinSide) return null;
            }
            // A card's outline is convex, and its blob fills a good part of it.
            float area = 0;
            var turn = 0;
            for (var i = 0; i < 4; i++)
            {
                var p = quad[i];
                var q = quad[(i + 1) % 4];
                var r = quad[(i + 2) % 4];
                var cross = (q.X - p.X) * (r.Y - q.Y) - (q.Y - p.Y) * (r.X - q.X);
                turn += Math.Sign(cross);
                area += p.X * q.Y - q.X * p.Y;
            }
            if (Math.Abs(turn) != 4) return null;
            area = Math.Abs(area) / 2;
            if (count < area * 0.3f || count > area * 1.1f) return null;

            // Clockwise as seen in the picture (y down), starting nearest the top left.
            if (turn < 0) Array.Reverse(quad);
            var first = 0;
            for (var i = 1; i < 4; i++)
            {
                if (quad[i].X + quad[i].Y < quad[first].X + quad[first].Y) first = i;
            }
            return new[] { quad[first], quad[(first + 1) % 4], quad[(first + 2) % 4], quad[(first + 3) % 4] };
        }

        private Vector2 Farthest(int count, Vector2 from)
        {
            var best = from;
            var bestDistance = -1f;
            for (var k = 0; k < count; k++)
            {
                var p = new Vector2(pixels[k] % width, pixels[k] / width);
                var d = Vector2.DistanceSquared(p, from);
                if (d <= bestDistance) continue;
                bestDistance = d;
                best = p;
            }
            return best;
        }

        /// <summary>
        /// Sets each corner where the card's edges, found to a fraction of a pixel by where the
        /// brightness changes fastest across them, meet. Null if an edge can't be found.
        /// </summary>
        private static Vector2[] Refine(GrayImage image, Vector2[] quad)
        {
            var lines = new (Vector2 point, Vector2 direction)[4];
            for (var i = 0; i < 4; i++)
            {
                var a = quad[i];
                var b = quad[(i + 1) % 4];
                var along = Vector2.Normalize(b - a);
                var normal = new Vector2(-along.Y, along.X);
                var points = new List<Vector2>();
                for (var k = 1; k < 12; k++)
                {
                    var p = Vector2.Lerp(a, b, k / 12f);
                    // The strongest change in brightness within a few pixels across the edge.
                    var best = 0f;
                    var bestOffset = float.NaN;
                    for (var o = -3f; o <= 3f; o += 0.25f)
                    {
                        var q = p + normal * o;
                        var change = Math.Abs(Sample(image, q.X + normal.X * 0.5f, q.Y + normal.Y * 0.5f) - Sample(image, q.X - normal.X * 0.5f, q.Y - normal.Y * 0.5f));
                        if (change <= best) continue;
                        best = change;
                        bestOffset = o;
                    }
                    if (best > 10f) points.Add(p + normal * bestOffset);
                }
                if (points.Count < 5) return null;
                lines[i] = FitLine(points);
            }
            var refined = new Vector2[4];
            for (var i = 0; i < 4; i++)
            {
                var corner = Intersect(lines[(i + 3) % 4], lines[i]);
                if (corner == null || Vector2.Distance(corner.Value, quad[i]) > 4f) return null;
                refined[i] = corner.Value;
            }
            return refined;
        }

        private static (Vector2 point, Vector2 direction) FitLine(List<Vector2> points)
        {
            var mean = Vector2.Zero;
            foreach (var p in points) mean += p;
            mean /= points.Count;
            float xx = 0, xy = 0, yy = 0;
            foreach (var p in points)
            {
                var d = p - mean;
                xx += d.X * d.X;
                xy += d.X * d.Y;
                yy += d.Y * d.Y;
            }
            // The main direction of the points, from the 2 x 2 covariance.
            var angle = 0.5f * (float)Math.Atan2(2 * xy, xx - yy);
            return (mean, new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)));
        }

        private static Vector2? Intersect((Vector2 point, Vector2 direction) a, (Vector2 point, Vector2 direction) b)
        {
            var cross = a.direction.X * b.direction.Y - a.direction.Y * b.direction.X;
            if (Math.Abs(cross) < 1e-4f) return null;
            var d = b.point - a.point;
            var t = (d.X * b.direction.Y - d.Y * b.direction.X) / cross;
            return a.point + a.direction * t;
        }

        /// <summary>Brightness at (x, y), blended between the four nearest pixels.</summary>
        private static float Sample(GrayImage image, float x, float y)
        {
            x = Math.Max(0, Math.Min(image.Width - 1.001f, x));
            y = Math.Max(0, Math.Min(image.Height - 1.001f, y));
            int x0 = (int)x, y0 = (int)y;
            float tx = x - x0, ty = y - y0;
            var top = image[x0, y0] * (1 - tx) + image[x0 + 1, y0] * tx;
            var bottom = image[x0, y0 + 1] * (1 - tx) + image[x0 + 1, y0 + 1] * tx;
            return top * (1 - ty) + bottom * ty;
        }
    }

    /// <summary>A plane-to-picture mapping: where points on a flat square land in a picture of it.</summary>
    public sealed class Homography
    {
        private readonly double[] h;

        private Homography(double[] h) => this.h = h;

        public Vector2 Map(float u, float v)
        {
            var w = h[6] * u + h[7] * v + h[8];
            return new Vector2((float)((h[0] * u + h[1] * v + h[2]) / w), (float)((h[3] * u + h[4] * v + h[5]) / w));
        }

        /// <summary>
        /// The mapping taking the square from (0, 0) to (<paramref name="size"/>, <paramref name="size"/>)
        /// to <paramref name="corners"/>: its (0, 0), (size, 0), (size, size) and (0, size) corners in turn.
        /// </summary>
        public static Homography FromSquare(Vector2[] corners, float size)
        {
            var from = new[] { (0f, 0f), (size, 0f), (size, size), (0f, size) };
            // Eight equations in h0..h7, with h8 = 1.
            var a = new double[8, 9];
            for (var i = 0; i < 4; i++)
            {
                var (u, v) = from[i];
                double x = corners[i].X, y = corners[i].Y;
                var r = 2 * i;
                a[r, 0] = u; a[r, 1] = v; a[r, 2] = 1; a[r, 6] = -x * u; a[r, 7] = -x * v; a[r, 8] = x;
                a[r + 1, 3] = u; a[r + 1, 4] = v; a[r + 1, 5] = 1; a[r + 1, 6] = -y * u; a[r + 1, 7] = -y * v; a[r + 1, 8] = y;
            }
            // Gaussian elimination with partial pivoting.
            for (var col = 0; col < 8; col++)
            {
                var pivot = col;
                for (var r = col + 1; r < 8; r++) if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col])) pivot = r;
                for (var c = 0; c < 9; c++) (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
                for (var r = 0; r < 8; r++)
                {
                    if (r == col || a[col, col] == 0) continue;
                    var f = a[r, col] / a[col, col];
                    for (var c = col; c < 9; c++) a[r, c] -= f * a[col, c];
                }
            }
            var h = new double[9];
            for (var i = 0; i < 8; i++) h[i] = a[i, 8] / a[i, i];
            h[8] = 1;
            return new Homography(h);
        }
    }
}

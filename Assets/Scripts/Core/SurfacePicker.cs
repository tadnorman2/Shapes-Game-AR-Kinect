using System;
using System.Collections.Generic;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>A flat surface the player picked: its plane, and how far it reaches.</summary>
    public sealed class PickedSurface
    {
        /// <summary>Coordinates on the surface, with the origin where it was picked and Y out of it, toward the Kinect.</summary>
        public TableFrame Frame { get; }
        /// <summary>Which cells of <see cref="Grid"/>, in <see cref="Frame"/>'s coordinates, the surface covers.</summary>
        public Grid Grid { get; }
        private readonly bool[] cells;

        public PickedSurface(TableFrame frame, Grid grid, bool[] cells)
        {
            Frame = frame;
            Grid = grid;
            this.cells = cells;
        }

        public bool Contains(float x, float z)
        {
            var cell = Grid.CellAt(x, z);
            return cell >= 0 && cells[cell];
        }

        public bool IsSurface(int cell) => cells[cell];

        /// <summary>The surface's area in view, in square meters.</summary>
        public float Area
        {
            get
            {
                var count = 0;
                foreach (var c in cells) if (c) count++;
                return count * Grid.CellSize * Grid.CellSize;
            }
        }

        /// <summary>Calls <paramref name="segment"/> with each piece of the line around the surface's edge.</summary>
        public void TraceEdge(Action<float, float, float, float> segment) => Outline.Trace(Grid, IsSurface, segment);
    }

    /// <summary>
    /// Finds the flat surface at a point the player picks in the Kinect's view. The plane comes
    /// from the depth right around that point, so it doesn't matter which way the Kinect is
    /// tilted, or what else is in view; the surface then spreads from there across the depth
    /// picture as far as it stays on that plane, without jumps in depth.
    /// </summary>
    public static class SurfacePicker
    {
        /// <summary>How far a point can be from the plane and still be on the surface, at <paramref name="depth"/> meters away.</summary>
        public static float Tolerance(float depth) => 0.01f + 0.004f * depth * depth;

        /// <summary>How far apart in depth two neighboring samples can be and still be one surface.</summary>
        private static float Continuity(float depth) => 0.02f + 0.02f * depth * depth;

        /// <summary>Points within this many samples of the picked one set the plane.</summary>
        private const int PatchRadius = 5;
        /// <summary>A surface tilted more than this many degrees from level, by the Kinect's tilt sensor, isn't one to play on.</summary>
        private const float MaxTilt = 45f;
        private const float MaxDistance = 4f;

        /// <summary>
        /// The flat surface at pixel (<paramref name="x"/>, <paramref name="y"/>) of
        /// <paramref name="depth"/>, looking at every <paramref name="step"/>th pixel, or null if
        /// there's no reading there, it isn't flat, or it's a wall rather than something level.
        /// </summary>
        public static PickedSurface Pick(DepthFrame depth, int x, int y, int step = 2)
        {
            int columns = depth.Width / step, rows = depth.Height / step;
            Vector3 At(int c, int r) => depth[c * step, r * step];
            bool Usable(Vector3 p) => DepthFrame.IsValid(p) && p.Z <= MaxDistance;

            // The nearest reading to where the player picked.
            int column = Math.Min(columns - 1, Math.Max(0, x / step)), row = Math.Min(rows - 1, Math.Max(0, y / step));
            if (!Usable(At(column, row)))
            {
                var found = false;
                for (var radius = 1; radius <= 4 && !found; radius++)
                {
                    for (var dr = -radius; dr <= radius && !found; dr++)
                    {
                        for (var dc = -radius; dc <= radius && !found; dc++)
                        {
                            int r = row + dr, c = column + dc;
                            if (r < 0 || r >= rows || c < 0 || c >= columns || !Usable(At(c, r))) continue;
                            row = r;
                            column = c;
                            found = true;
                        }
                    }
                }
                if (!found) return null;
            }
            var center = At(column, row);

            // The plane from the patch around it, all on one surface with it.
            var patch = new List<Vector3>();
            for (var dr = -PatchRadius; dr <= PatchRadius; dr++)
            {
                for (var dc = -PatchRadius; dc <= PatchRadius; dc++)
                {
                    int r = row + dr, c = column + dc;
                    if (r < 0 || r >= rows || c < 0 || c >= columns) continue;
                    var p = At(c, r);
                    if (Usable(p) && Math.Abs(p.Z - center.Z) < Continuity(center.Z) * PatchRadius) patch.Add(p);
                }
            }
            if (patch.Count < 15) return null;
            var plane = Fit(patch);
            foreach (var p in patch)
            {
                // Not flat: a corner, an edge, a cushion.
                if (Math.Abs(plane.SignedDistance(p)) > Tolerance(p.Z) * 1.5f) return null;
            }

            // Spread over the depth picture along the plane, then fit the plane to all of it and
            // spread again, so the surface's far reaches set its tilt too.
            var region = Grow(columns, rows, At, column, row, plane);
            var points = new List<Vector3>();
            foreach (var i in region) points.Add(At(i % columns, i / columns));
            if (points.Count > patch.Count) plane = Fit(points);
            region = Grow(columns, rows, At, column, row, plane);

            // A wall is flat too, but no place for a board. The Kinect's tilt sensor can be well
            // out, so this only rules out surfaces facing well sideways.
            if (depth.UpHint is Vector3 hint && Math.Abs(hint.Length() - 1f) < 0.1f &&
                Vector3.Dot(plane.Normal, Vector3.Normalize(hint)) < Math.Cos(MaxTilt * Math.PI / 180.0)) return null;

            var origin = center - plane.Normal * plane.SignedDistance(center);
            var frame = new TableFrame(origin, plane.Normal);
            var grid = new Grid(4f, 0.02f);
            var cells = new bool[grid.CellCount];
            foreach (var i in region)
            {
                var t = frame.ToTable(At(i % columns, i / columns));
                var cell = grid.CellAt(t.X, t.Z);
                if (cell >= 0) cells[cell] = true;
            }
            cells = GridShapes.BiggestPiece(grid, GridShapes.Close(grid, cells)) ?? cells;
            return new PickedSurface(frame, grid, cells);
        }

        /// <summary>The samples joined to (<paramref name="column"/>, <paramref name="row"/>) without leaving <paramref name="plane"/> or jumping in depth.</summary>
        private static List<int> Grow(int columns, int rows, Func<int, int, Vector3> at, int column, int row, Plane plane)
        {
            var visited = new bool[columns * rows];
            var region = new List<int>();
            var queue = new Queue<int>();
            var start = row * columns + column;
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                region.Add(i);
                int c = i % columns, r = i / columns;
                var p = at(c, r);
                foreach (var (dc, dr) in Neighbors)
                {
                    int nc = c + dc, nr = r + dr;
                    if (nc < 0 || nc >= columns || nr < 0 || nr >= rows) continue;
                    var j = nr * columns + nc;
                    if (visited[j]) continue;
                    var q = at(nc, nr);
                    if (!DepthFrame.IsValid(q) || q.Z > MaxDistance || Math.Abs(q.Z - p.Z) > Continuity(p.Z) ||
                        Math.Abs(plane.SignedDistance(q)) > Tolerance(q.Z)) continue;
                    visited[j] = true;
                    queue.Enqueue(j);
                }
            }
            return region;
        }

        private static readonly (int, int)[] Neighbors = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        /// <summary>The least-squares plane through <paramref name="points"/>, its normal facing the Kinect.</summary>
        public static Plane Fit(IReadOnlyList<Vector3> points)
        {
            var mean = Vector3.Zero;
            foreach (var p in points) mean += p;
            mean /= points.Count;
            // Covariance of the points; the plane's normal is its least-spread direction.
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in points)
            {
                var d = p - mean;
                xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z;
                yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
            }
            var v = LinearAlgebra.SmallestEigenvector(new[,] { { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } });
            var normal = Vector3.Normalize(new Vector3((float)v[0], (float)v[1], (float)v[2]));
            // The Kinect is at the origin, so the side facing it has the normal pointing back toward it.
            if (Vector3.Dot(normal, mean) > 0f) normal = -normal;
            return new Plane(normal, Vector3.Dot(normal, mean));
        }
    }

    /// <summary>A plane: points p with Dot(Normal, p) == Distance.</summary>
    public readonly struct Plane
    {
        public Vector3 Normal { get; }
        public float Distance { get; }

        public Plane(Vector3 normal, float distance)
        {
            Normal = normal;
            Distance = distance;
        }

        public float SignedDistance(Vector3 point) => Vector3.Dot(Normal, point) - Distance;
    }
}

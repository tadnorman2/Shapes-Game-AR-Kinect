using System;
using System.Collections.Generic;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>A flat, level surface the Kinect sees: a table, the floor, a shelf.</summary>
    public sealed class LevelSurface
    {
        /// <summary>The surface's plane, with its normal pointing up.</summary>
        public Plane Plane { get; }
        /// <summary>The middle of the points on it, in camera space.</summary>
        public Vector3 Middle { get; }
        /// <summary>Roughly how much of it is in view, in square meters.</summary>
        public float Area { get; }
        /// <summary>How far below the Kinect it is, in meters.</summary>
        public float Depth => Plane.Distance < 0 ? -Plane.Distance : 0f;

        public LevelSurface(Plane plane, Vector3 middle, float area)
        {
            Plane = plane;
            Middle = middle;
            Area = area;
        }

        public override string ToString() =>
            $"{Area:0.00} m2, {Depth:0.00} m below the Kinect, {Middle.Z:0.00} m out";
    }

    /// <summary>
    /// Finds the table in a depth image. All level surfaces in a room are parallel, so it first
    /// works out which way is up from the direction most of the image's flat patches face, then
    /// sorts those patches by height and takes the highest surface that's big enough to play on:
    /// the floor and anything else lower than the table are skipped, and walls never count since
    /// they don't face up.
    ///
    /// Works however steeply the Kinect looks down, from a few degrees to straight down, and
    /// however much of the picture the table fills.
    /// </summary>
    public static class TableFinder
    {
        /// <summary>The Kinect v2 depth camera's focal length, in pixels at 512 wide.</summary>
        private const float FocalAt512 = 365f;
        /// <summary>Depth past this is too rough to judge a surface by, in meters.</summary>
        public const float MaxDistance = 4f;
        /// <summary>About 30 by 30 cm: anything smaller leaves no room to play.</summary>
        public const float MinArea = 0.09f;

        /// <summary>
        /// How far a point can be from a plane and still count as on it, in meters, at
        /// <paramref name="depth"/> meters away: the Kinect's depth gets rougher with distance.
        /// </summary>
        public static float Tolerance(float depth) => 0.01f + 0.004f * depth * depth;

        /// <summary>
        /// The table in <paramref name="depth"/>, or null. <paramref name="found"/>, if given, gets
        /// every level surface seen, highest first, and <paramref name="report"/> what was tried.
        /// </summary>
        public static LevelSurface Find(DepthFrame depth, List<LevelSurface> found = null, List<string> report = null)
        {
            var patches = FlatPatches(depth);
            report?.Add($"{patches.Count} flat patches");
            var up = FindUp(patches, depth.UpHint, report);
            if (up == null) return null;
            var surfaces = LevelSurfaces(depth, patches, up.Value);
            found?.AddRange(surfaces);
            foreach (var s in surfaces) report?.Add($"level surface: {s}");
            foreach (var s in surfaces)
            {
                if (s.Area >= MinArea) return s;
            }
            return null;
        }

        /// <summary>A small flat piece of what the Kinect sees: a point, its normal, and the area it stands for.</summary>
        private struct Patch
        {
            public Vector3 Point;
            public Vector3 Normal;
            public float Area;
        }

        /// <summary>
        /// Every few pixels, the normal of the surface there from its neighbors, kept where the
        /// neighbors agree it's flat (so not at edges). Normals face the camera.
        /// </summary>
        private static List<Patch> FlatPatches(DepthFrame depth)
        {
            var patches = new List<Patch>();
            var step = Math.Max(1, depth.Width / 256);
            var reach = 3 * step;
            var focal = FocalAt512 * depth.Width / 512f;
            for (var y = reach; y < depth.Height - reach; y += step)
            {
                for (var x = reach; x < depth.Width - reach; x += step)
                {
                    var p = depth[x, y];
                    if (!DepthFrame.IsValid(p) || p.Z > MaxDistance) continue;
                    var left = depth[x - reach, y];
                    var right = depth[x + reach, y];
                    var above = depth[x, y - reach];
                    var below = depth[x, y + reach];
                    if (!DepthFrame.IsValid(left) || !DepthFrame.IsValid(right) ||
                        !DepthFrame.IsValid(above) || !DepthFrame.IsValid(below)) continue;
                    var normal = Vector3.Cross(right - left, below - above);
                    if (normal.LengthSquared() < 1e-12f) continue;
                    normal = Vector3.Normalize(normal);
                    if (Vector3.Dot(normal, p) > 0f) normal = -normal;
                    var tolerance = Tolerance(p.Z) * 0.5f;
                    if (Math.Abs(Vector3.Dot(normal, left - p)) > tolerance ||
                        Math.Abs(Vector3.Dot(normal, right - p)) > tolerance ||
                        Math.Abs(Vector3.Dot(normal, above - p)) > tolerance ||
                        Math.Abs(Vector3.Dot(normal, below - p)) > tolerance) continue;
                    // A pixel covers more of a surface the further away it is and the more the
                    // surface is turned away from the camera.
                    var facing = Math.Max(0.1f, Math.Abs(Vector3.Dot(normal, Vector3.Normalize(p))));
                    var size = p.Z * step / focal;
                    patches.Add(new Patch { Point = p, Normal = normal, Area = size * size / facing });
                }
            }
            return patches;
        }

        /// <summary>
        /// Which way is up, in camera space: the direction the most area of flat patches faces.
        /// With the Kinect's own idea of up (<paramref name="hint"/>, from its tilt sensor), only
        /// patches near that count, which rules out walls whichever way the Kinect is tilted;
        /// without, any patch facing the camera's upper side does.
        /// </summary>
        private static Vector3? FindUp(List<Patch> patches, Vector3? hint, List<string> report)
        {
            if (hint != null && Math.Abs(hint.Value.Length() - 1f) < 0.1f)
            {
                var near = MostCommonNormal(patches, Vector3.Normalize(hint.Value), 25f);
                report?.Add(near != null
                    ? $"up {Format(near.Value)}, near the Kinect's own {Format(hint.Value)}"
                    : $"no level surfaces near the Kinect's up {Format(hint.Value)}");
                return near;
            }
            var up = MostCommonNormal(patches, new Vector3(0f, 1f, 0f), 85f);
            report?.Add(up != null ? $"up {Format(up.Value)}, without the Kinect's own" : "nothing faces up");
            return up;
        }

        /// <summary>
        /// Of the patches whose normals are within <paramref name="maxAngle"/> degrees of
        /// <paramref name="around"/>, the direction shared by the most area, averaged; or null.
        /// </summary>
        private static Vector3? MostCommonNormal(List<Patch> patches, Vector3 around, float maxAngle)
        {
            var minAround = (float)Math.Cos(maxAngle * Math.PI / 180.0);
            var candidates = new List<Patch>();
            var total = 0f;
            foreach (var patch in patches)
            {
                if (Vector3.Dot(patch.Normal, around) < minAround) continue;
                candidates.Add(patch);
                total += patch.Area;
            }
            if (candidates.Count < 20 || total < MinArea / 4) return null;

            var random = new Random(1);
            var near = (float)Math.Cos(8.0 * Math.PI / 180.0);
            Vector3? best = null;
            var bestArea = 0f;
            for (var i = 0; i < 200; i++)
            {
                var seed = candidates[random.Next(candidates.Count)].Normal;
                var area = 0f;
                foreach (var c in candidates)
                {
                    if (Vector3.Dot(c.Normal, seed) > near) area += c.Area;
                }
                if (area > bestArea)
                {
                    bestArea = area;
                    best = seed;
                }
            }
            if (best == null) return null;
            // Average the normals near the winner, twice, closing in.
            var up = best.Value;
            foreach (var degrees in new[] { 8.0, 4.0 })
            {
                var limit = (float)Math.Cos(degrees * Math.PI / 180.0);
                var sum = Vector3.Zero;
                foreach (var c in candidates)
                {
                    if (Vector3.Dot(c.Normal, up) > limit) sum += c.Normal * c.Area;
                }
                if (sum.LengthSquared() > 0f) up = Vector3.Normalize(sum);
            }
            return up;
        }

        /// <summary>
        /// The level surfaces in the picture, highest first: heights where lots of up-facing area
        /// is, each fitted with its own plane and measured.
        /// </summary>
        private static List<LevelSurface> LevelSurfaces(DepthFrame depth, List<Patch> patches, Vector3 up)
        {
            // Area of up-facing patches by height, in 1 cm steps.
            const float bin = 0.01f;
            var level = (float)Math.Cos(12.0 * Math.PI / 180.0);
            var heights = new List<(float height, Patch patch)>();
            var lowest = float.MaxValue;
            var highest = float.MinValue;
            foreach (var patch in patches)
            {
                if (Vector3.Dot(patch.Normal, up) < level) continue;
                var h = Vector3.Dot(up, patch.Point);
                heights.Add((h, patch));
                lowest = Math.Min(lowest, h);
                highest = Math.Max(highest, h);
            }
            var surfaces = new List<LevelSurface>();
            if (heights.Count == 0) return surfaces;
            var bins = (int)((highest - lowest) / bin) + 1;
            var area = new float[bins];
            foreach (var (h, patch) in heights) area[(int)((h - lowest) / bin)] += patch.Area;
            var smooth = new float[bins];
            for (var i = 0; i < bins; i++)
            {
                for (var d = -2; d <= 2; d++)
                {
                    var j = i + d;
                    if (j >= 0 && j < bins) smooth[i] += area[j] * (3 - Math.Abs(d));
                }
            }

            // Each peak, from the top down, unless it's part of a surface already found.
            var taken = new bool[bins];
            for (var i = bins - 1; i >= 0; i--)
            {
                if (taken[i] || smooth[i] < MinArea / 2) continue;
                var isPeak = true;
                for (var d = -3; d <= 3 && isPeak; d++)
                {
                    var j = i + d;
                    if (d != 0 && j >= 0 && j < bins && !taken[j] && smooth[j] > smooth[i]) isPeak = false;
                }
                if (!isPeak) continue;
                var surface = Measure(depth, heights, lowest + (i + 0.5f) * bin, up);
                // Mark this surface's heights so its shoulders aren't found again.
                for (var j = Math.Max(0, i - 4); j <= Math.Min(bins - 1, i + 4); j++) taken[j] = true;
                if (surface != null && !surfaces.Exists(other => Math.Abs(other.Plane.Distance - surface.Plane.Distance) < 0.03f))
                {
                    surfaces.Add(surface);
                }
            }
            surfaces.Sort((a, b) => b.Plane.Distance.CompareTo(a.Plane.Distance));
            return surfaces;
        }

        /// <summary>
        /// Fits a plane to the up-facing patches near <paramref name="height"/>, then measures the
        /// biggest connected piece of everything on that plane.
        /// </summary>
        private static LevelSurface Measure(DepthFrame depth, List<(float height, Patch patch)> heights, float height, Vector3 up)
        {
            var points = new List<Vector3>();
            foreach (var (h, patch) in heights)
            {
                if (Math.Abs(h - height) < 0.03f) points.Add(patch.Point);
            }
            if (points.Count < 10) return null;
            var plane = new Plane(up, height);
            // Refit twice: a tiny tilt in "up" adds up over a meter or two of table.
            for (var i = 0; i < 2; i++) plane = Fit(points, plane);
            var piece = Piece(depth, plane, out var middle);
            if (piece == null) return null;
            return new LevelSurface(plane, middle, piece.Area);
        }

        /// <summary>Least-squares plane through the points near <paramref name="rough"/>, normal kept on its side.</summary>
        private static Plane Fit(List<Vector3> points, Plane rough)
        {
            var near = new List<Vector3>();
            foreach (var p in points)
            {
                if (Math.Abs(rough.SignedDistance(p)) < Tolerance(p.Z) * 1.5f) near.Add(p);
            }
            if (near.Count < 10) return rough;
            var fitted = PlaneFit.LeastSquares(near);
            var normal = Vector3.Dot(fitted.Normal, rough.Normal) < 0f ? -fitted.Normal : fitted.Normal;
            var mean = Vector3.Zero;
            foreach (var p in near) mean += p;
            mean /= near.Count;
            return new Plane(normal, Vector3.Dot(normal, mean));
        }

        /// <summary>Cells on a table-aligned grid, with which are on the surface.</summary>
        public sealed class SurfaceCells
        {
            public TableFrame Frame;
            public Grid Grid;
            public bool[] Cells;
            public float Area;
        }

        /// <summary>
        /// The biggest connected piece of what's on <paramref name="plane"/>, on a grid of
        /// <paramref name="cellSize"/> cells centered on it; or null if nothing is.
        /// </summary>
        public static SurfaceCells Piece(DepthFrame depth, Plane plane, out Vector3 middle, float cellSize = 0.02f, float gridSize = 4f)
        {
            middle = Vector3.Zero;
            // Center the grid on the points on the plane, so a surface far from the Kinect fits.
            var center = Vector3.Zero;
            var count = 0;
            var step = Math.Max(1, depth.Width / 256);
            for (var y = 0; y < depth.Height; y += step)
            {
                for (var x = 0; x < depth.Width; x += step)
                {
                    var p = depth[x, y];
                    if (!OnPlane(plane, p)) continue;
                    center += p;
                    count++;
                }
            }
            if (count == 0) return null;
            center /= count;
            center -= plane.Normal * plane.SignedDistance(center);

            var frame = new TableFrame(center, plane.Normal);
            var grid = new Grid(gridSize, cellSize);
            var marked = new bool[grid.CellCount];
            foreach (var p in depth.Points)
            {
                if (!OnPlane(plane, p)) continue;
                var t = frame.ToTable(p);
                var cell = grid.CellAt(t.X, t.Z);
                if (cell >= 0) marked[cell] = true;
            }
            marked = GridShapes.Close(grid, marked);
            var piece = GridShapes.BiggestPiece(grid, marked);
            if (piece == null) return null;

            float sumX = 0, sumZ = 0;
            var cells = 0;
            for (var i = 0; i < piece.Length; i++)
            {
                if (!piece[i]) continue;
                sumX += grid.CenterX(i);
                sumZ += grid.CenterZ(i);
                cells++;
            }
            middle = frame.ToCamera(new Vector3(sumX / cells, 0f, sumZ / cells));
            return new SurfaceCells { Frame = frame, Grid = grid, Cells = piece, Area = cells * cellSize * cellSize };
        }

        private static bool OnPlane(Plane plane, Vector3 p) =>
            DepthFrame.IsValid(p) && p.Z <= MaxDistance && Math.Abs(plane.SignedDistance(p)) < Tolerance(p.Z);

        private static string Format(Vector3 v) => $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})";
    }
}

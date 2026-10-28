using System;
using System.Collections.Generic;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>
    /// Works out which way is up in the Kinect's view when the Kinect doesn't say (its tilt
    /// sensor usually does, through <see cref="DepthFrame.UpHint"/>): the direction the most area
    /// of flat patches in the picture faces, since tables and floors are level.
    /// </summary>
    public static class UpFinder
    {
        /// <summary>The Kinect v2 depth camera's focal length, in pixels at 512 wide.</summary>
        private const float FocalAt512 = 365f;
        /// <summary>Depth past this is too rough to judge a surface by, in meters.</summary>
        private const float MaxDistance = 4f;
        private const float MinArea = SurfaceMap.MinArea;

        /// <summary>How far a point can be from a flat surface and still count as on it, at <paramref name="depth"/> meters away.</summary>
        private static float Tolerance(float depth) => 0.01f + 0.004f * depth * depth;

        /// <summary>Which way is up in camera space, from <paramref name="depth"/>, or null if nothing in view is level.</summary>
        public static Vector3? Find(DepthFrame depth, List<string> report = null) => FindUp(FlatPatches(depth), depth.UpHint, report);

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

        private static string Format(Vector3 v) => $"({v.X:0.00}, {v.Y:0.00}, {v.Z:0.00})";
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;

namespace ShapesKinect.Core
{
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

    /// <summary>
    /// Finds the table in a depth image: the biggest flat surface that faces up, by RANSAC, then
    /// refined by least squares over the points on it.
    ///
    /// Each plane's normal is taken on the side facing the camera. A table seen from above then
    /// points up, while a wall seen from the front points down and back toward the camera, even
    /// when the camera is tilted so that both are at the same angle to its own up direction.
    /// </summary>
    public static class PlaneFit
    {
        /// <summary>Points this close to a plane count as on it, in meters.</summary>
        public const float InlierDistance = 0.012f;

        /// <summary>
        /// The biggest plane among <paramref name="points"/> whose camera-facing normal is within
        /// <paramref name="maxTiltDegrees"/> of camera-space <paramref name="up"/>, or null if
        /// there's no such plane.
        /// </summary>
        public static Plane? FindUpFacing(IReadOnlyList<Vector3> points, Vector3 up, float maxTiltDegrees = 85f,
            int iterations = 300, Random random = null)
        {
            random = random ?? new Random(1);
            if (points.Count < 50) return null;
            var minUp = (float)Math.Cos(maxTiltDegrees * Math.PI / 180.0);
            Plane? best = null;
            var bestCount = 0;
            for (var i = 0; i < iterations; i++)
            {
                var a = points[random.Next(points.Count)];
                var b = points[random.Next(points.Count)];
                var c = points[random.Next(points.Count)];
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.LengthSquared() < 1e-8f) continue;
                normal = TowardCamera(Vector3.Normalize(normal), a);
                if (Vector3.Dot(normal, up) < minUp) continue;
                var plane = new Plane(normal, Vector3.Dot(normal, a));
                var count = CountInliers(points, plane, bestCount);
                if (count > bestCount)
                {
                    bestCount = count;
                    best = plane;
                }
            }
            if (best == null) return null;
            return Refine(points, best.Value);
        }

        /// <summary>
        /// The table among <paramref name="points"/>: of the few biggest up-facing flat surfaces,
        /// the highest one that's at least a quarter the size of the biggest. From an angled view the
        /// floor past the table can fill more of the picture than the table itself, but the table
        /// is always above it.
        /// </summary>
        public static Plane? FindTable(IReadOnlyList<Vector3> points, Vector3 up, Random random = null)
        {
            random = random ?? new Random(1);
            var remaining = new List<Vector3>(points);
            var found = new List<(Plane plane, int count)>();
            for (var i = 0; i < 3; i++)
            {
                var plane = FindUpFacing(remaining, up, random: random);
                if (plane == null) break;
                var rest = new List<Vector3>(remaining.Count);
                var count = 0;
                foreach (var p in remaining)
                {
                    if (Math.Abs(plane.Value.SignedDistance(p)) < InlierDistance) count++;
                    else rest.Add(p);
                }
                found.Add((plane.Value, count));
                remaining = rest;
            }
            if (found.Count == 0) return null;
            var biggest = 0;
            foreach (var f in found) biggest = Math.Max(biggest, f.count);
            Plane? best = null;
            foreach (var f in found)
            {
                if (f.count * 4 < biggest) continue;
                // With normals facing the camera, Distance is minus how far below the camera the
                // plane is, so the higher plane has the larger Distance.
                if (best == null || f.plane.Distance > best.Value.Distance) best = f.plane;
            }
            return best;
        }

        /// <summary>Counts points near <paramref name="plane"/>; stops early once it can't beat <paramref name="toBeat"/>.</summary>
        private static int CountInliers(IReadOnlyList<Vector3> points, Plane plane, int toBeat)
        {
            var count = 0;
            for (var i = 0; i < points.Count; i++)
            {
                if (Math.Abs(plane.SignedDistance(points[i])) < InlierDistance) count++;
                else if (count + (points.Count - i - 1) <= toBeat) return count;
            }
            return count;
        }

        /// <summary>Least-squares plane through the points near <paramref name="rough"/>.</summary>
        public static Plane Refine(IReadOnlyList<Vector3> points, Plane rough)
        {
            var inliers = new List<Vector3>();
            foreach (var p in points)
            {
                if (Math.Abs(rough.SignedDistance(p)) < InlierDistance) inliers.Add(p);
            }
            if (inliers.Count < 3) return rough;
            var mean = Vector3.Zero;
            foreach (var p in inliers) mean += p;
            mean /= inliers.Count;
            // Covariance of the points; the plane's normal is its least-spread direction.
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in inliers)
            {
                var d = p - mean;
                xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z;
                yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z;
            }
            var normal = TowardCamera(SmallestEigenvector(new[,] { { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } }), mean);
            return new Plane(normal, Vector3.Dot(normal, mean));
        }

        /// <summary>
        /// Flips <paramref name="normal"/> of the plane through <paramref name="onPlane"/>, if needed,
        /// so it points to the camera's side. The camera is at the origin of camera space.
        /// </summary>
        private static Vector3 TowardCamera(Vector3 normal, Vector3 onPlane) =>
            Vector3.Dot(normal, onPlane) > 0f ? -normal : normal;

        /// <summary>The eigenvector of a symmetric 3x3 matrix with the smallest eigenvalue, by Jacobi rotations.</summary>
        internal static Vector3 SmallestEigenvector(double[,] matrix)
        {
            var a = (double[,])matrix.Clone();
            var v = new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (var sweep = 0; sweep < 50; sweep++)
            {
                var off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                if (off < 1e-15) break;
                for (var p = 0; p < 2; p++)
                {
                    for (var q = p + 1; q < 3; q++)
                    {
                        if (Math.Abs(a[p, q]) < 1e-18) continue;
                        var theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                        var t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                        if (theta == 0) t = 1;
                        var c = 1 / Math.Sqrt(t * t + 1);
                        var s = t * c;
                        for (var k = 0; k < 3; k++)
                        {
                            var akp = a[k, p];
                            var akq = a[k, q];
                            a[k, p] = c * akp - s * akq;
                            a[k, q] = s * akp + c * akq;
                        }
                        for (var k = 0; k < 3; k++)
                        {
                            var apk = a[p, k];
                            var aqk = a[q, k];
                            a[p, k] = c * apk - s * aqk;
                            a[q, k] = s * apk + c * aqk;
                        }
                        for (var k = 0; k < 3; k++)
                        {
                            var vkp = v[k, p];
                            var vkq = v[k, q];
                            v[k, p] = c * vkp - s * vkq;
                            v[k, q] = s * vkp + c * vkq;
                        }
                    }
                }
            }
            var smallest = 0;
            for (var i = 1; i < 3; i++)
            {
                if (a[i, i] < a[smallest, smallest]) smallest = i;
            }
            return Vector3.Normalize(new Vector3((float)v[0, smallest], (float)v[1, smallest], (float)v[2, smallest]));
        }
    }
}

using System.Collections.Generic;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>Fitting planes to points.</summary>
    public static class PlaneFit
    {
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

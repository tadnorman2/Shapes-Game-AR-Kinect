using System;
using System.Collections.Generic;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>Where a card lies, in the Kinect's camera space.</summary>
    public sealed class CardPlacement
    {
        public int Id { get; }
        /// <summary>
        /// Coordinates on the card: the origin at its middle, Y up out of it, Z toward its printed
        /// top (away from its label), X to its right as printed.
        /// </summary>
        public TableFrame Frame { get; }
        /// <summary>The black square's side, as measured, in meters.</summary>
        public float Size { get; }
        /// <summary>Whether the Kinect's depth was used, rather than the picture alone.</summary>
        public bool FromDepth { get; }

        public CardPlacement(int id, TableFrame frame, float size, bool fromDepth)
        {
            Id = id;
            Frame = frame;
            Size = size;
            FromDepth = fromDepth;
        }
    }

    /// <summary>
    /// Works out where a card seen in the color picture lies. The depth camera gives the plane
    /// it lies on, from its readings inside the card; the card's corners are where the color
    /// camera's lines of sight through the corners seen meet that plane. That's steadier than the
    /// picture alone, which has to judge the card's tilt from how squashed it looks, and is used
    /// only when there's no depth there.
    /// </summary>
    public static class CardLocator
    {
        /// <summary>The printed black square's side, in meters, for when only the picture is there to go on.</summary>
        public const float PrintedSize = 0.15f;
        private const int MinDepthPoints = 12;

        public static CardPlacement Locate(CardSighting sighting, PinholeCamera camera, DepthFrame depth, int step = 2)
        {
            var corners = new Vector3[4];
            var fromDepth = false;
            var plane = depth == null ? null : DepthPlane(sighting, camera, depth, step);
            if (plane != null)
            {
                fromDepth = true;
                for (var i = 0; i < 4; i++)
                {
                    var ray = Ray(camera, sighting.Corners[i]);
                    var along = Vector3.Dot(plane.Value.Normal, ray);
                    if (Math.Abs(along) < 1e-4f) return null;
                    var t = (plane.Value.Distance - Vector3.Dot(plane.Value.Normal, camera.Position)) / along;
                    if (t <= 0f) return null;
                    corners[i] = camera.Position + ray * t;
                }
            }
            else
            {
                var fromPicture = FromPicture(sighting, camera);
                if (fromPicture == null) return null;
                corners = fromPicture;
            }

            var middle = (corners[0] + corners[1] + corners[2] + corners[3]) / 4;
            // Top left, top right, bottom right, bottom left.
            var right = (corners[1] - corners[0] + corners[2] - corners[3]) / 2;
            var toTop = (corners[0] - corners[3] + corners[1] - corners[2]) / 2;
            var up = Vector3.Normalize(Vector3.Cross(toTop, right));
            // Up faces the Kinect, which is at the origin: the card is seen from above.
            if (Vector3.Dot(up, middle) > 0f) up = -up;
            var size = (right.Length() + toTop.Length()) / 2;
            if (size < 0.03f || size > 0.5f) return null;
            return new CardPlacement(sighting.Id, new TableFrame(middle, up, toTop), size, fromDepth);
        }

        /// <summary>The color camera's line of sight through <paramref name="pixel"/>, in camera space; one unit along it is one unit of its depth.</summary>
        public static Vector3 Ray(PinholeCamera camera, Vector2 pixel)
        {
            var y = (pixel.Y - camera.Cy) / camera.Fy;
            var x = (pixel.X - camera.Cx - camera.Skew * y) / camera.Fx;
            return camera.Forward + camera.Right * x + camera.Down * y;
        }

        /// <summary>The plane through the depth readings that land inside the card, or null if there are too few or they aren't flat.</summary>
        private static Plane? DepthPlane(CardSighting sighting, PinholeCamera camera, DepthFrame depth, int step)
        {
            // Well inside the card, so readings from past its edges don't creep in.
            var middle = (sighting.Corners[0] + sighting.Corners[1] + sighting.Corners[2] + sighting.Corners[3]) / 4;
            var inner = new Vector2[4];
            for (var i = 0; i < 4; i++) inner[i] = middle + (sighting.Corners[i] - middle) * 0.8f;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var c in inner)
            {
                minX = Math.Min(minX, c.X); maxX = Math.Max(maxX, c.X);
                minY = Math.Min(minY, c.Y); maxY = Math.Max(maxY, c.Y);
            }
            var points = new List<Vector3>();
            for (var y = 0; y < depth.Height; y += step)
            {
                for (var x = 0; x < depth.Width; x += step)
                {
                    var p = depth[x, y];
                    if (!DepthFrame.IsValid(p)) continue;
                    var pixel = camera.Project(p);
                    if (pixel.X < minX || pixel.X > maxX || pixel.Y < minY || pixel.Y > maxY || !Inside(inner, pixel)) continue;
                    points.Add(p);
                }
            }
            if (points.Count < MinDepthPoints) return null;
            var plane = PlaneFit.Fit(points);
            // Once more without readings well off it, like a fingertip over the card.
            var kept = points.FindAll(p => Math.Abs(plane.SignedDistance(p)) < 0.01f + 0.004f * p.Z * p.Z);
            if (kept.Count < MinDepthPoints) return null;
            return PlaneFit.Fit(kept);
        }

        /// <summary>Whether <paramref name="p"/> is inside the convex four-cornered <paramref name="quad"/>.</summary>
        private static bool Inside(Vector2[] quad, Vector2 p)
        {
            var sign = 0;
            for (var i = 0; i < 4; i++)
            {
                var a = quad[i];
                var b = quad[(i + 1) % 4];
                var cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
                var s = Math.Sign(cross);
                if (s == 0) continue;
                if (sign == 0) sign = s;
                else if (s != sign) return false;
            }
            return true;
        }

        /// <summary>
        /// The card's corners from the picture alone, knowing its printed size: the plane-to-picture
        /// mapping of its square, taken apart into how it's turned and where it is.
        /// </summary>
        private static Vector3[] FromPicture(CardSighting sighting, PinholeCamera camera)
        {
            // Corners as directions from the camera, in its own right/down/forward terms.
            var normalized = new Vector2[4];
            for (var i = 0; i < 4; i++)
            {
                var c = sighting.Corners[i];
                var y = (c.Y - camera.Cy) / camera.Fy;
                normalized[i] = new Vector2((c.X - camera.Cx - camera.Skew * y) / camera.Fx, y);
            }
            // The square's corners (0,0), (1,0), (1,1), (0,1) map to those directions.
            var m = Solve(normalized);
            if (m == null) return null;
            var (r1, r2, t) = m.Value;
            // The unit square stands for the printed square, so its sides are that long.
            var scale = 2f * PrintedSize / (r1.Length() + r2.Length());
            r1 *= scale;
            r2 *= scale;
            t *= scale;
            if (t.Z < 0f)
            {
                r1 = -r1;
                r2 = -r2;
                t = -t;
            }
            var corners = new Vector3[4];
            var square = new[] { (0f, 0f), (1f, 0f), (1f, 1f), (0f, 1f) };
            for (var i = 0; i < 4; i++)
            {
                var (u, v) = square[i];
                var local = t + r1 * u + r2 * v;
                corners[i] = camera.Position + camera.Right * local.X + camera.Down * local.Y + camera.Forward * local.Z;
            }
            return corners;
        }

        /// <summary>
        /// The 3 x 3 matrix [r1 r2 t], up to scale, taking square point (u, v, 1) to the direction
        /// of corner i: solved directly from the four correspondences.
        /// </summary>
        private static (Vector3 r1, Vector3 r2, Vector3 t)? Solve(Vector2[] normalized)
        {
            var a = new double[8, 9];
            var square = new[] { (0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0) };
            for (var i = 0; i < 4; i++)
            {
                var (u, v) = square[i];
                double x = normalized[i].X, y = normalized[i].Y;
                var r = 2 * i;
                a[r, 0] = u; a[r, 1] = v; a[r, 2] = 1; a[r, 6] = -x * u; a[r, 7] = -x * v; a[r, 8] = x;
                a[r + 1, 3] = u; a[r + 1, 4] = v; a[r + 1, 5] = 1; a[r + 1, 6] = -y * u; a[r + 1, 7] = -y * v; a[r + 1, 8] = y;
            }
            for (var col = 0; col < 8; col++)
            {
                var pivot = col;
                for (var r = col + 1; r < 8; r++) if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col])) pivot = r;
                for (var c = 0; c < 9; c++) (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]);
                if (Math.Abs(a[col, col]) < 1e-12) return null;
                for (var r = 0; r < 8; r++)
                {
                    if (r == col) continue;
                    var f = a[r, col] / a[col, col];
                    for (var c = col; c < 9; c++) a[r, c] -= f * a[col, c];
                }
            }
            var h = new double[9];
            for (var i = 0; i < 8; i++) h[i] = a[i, 8] / a[i, i];
            h[8] = 1;
            // Columns: (h0, h3, h6), (h1, h4, h7), (h2, h5, h8).
            return (new Vector3((float)h[0], (float)h[3], (float)h[6]),
                new Vector3((float)h[1], (float)h[4], (float)h[7]),
                new Vector3((float)h[2], (float)h[5], (float)h[8]));
        }
    }
}

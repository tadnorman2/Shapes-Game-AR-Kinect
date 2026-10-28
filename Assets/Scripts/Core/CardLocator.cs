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
        /// <summary>Whether the Kinect's depth around the card was measured in this picture.</summary>
        public bool FromDepth { get; }

        /// <summary>The plane the card was placed on, measured by depth now or before, or null if placed by the picture alone.</summary>
        public Plane? Plane { get; }

        public CardPlacement(int id, TableFrame frame, float size, bool fromDepth, Plane? plane = null)
        {
            Id = id;
            Frame = frame;
            Size = size;
            FromDepth = fromDepth;
            Plane = plane;
        }
    }

    /// <summary>
    /// Works out where a card seen in the color picture lies. It lies on the table: level, by the
    /// Kinect's tilt sensor, at the height the depth camera measures around the card (or, without
    /// a tilt sensor, on the plane the depth around the card fits). The card's corners are where
    /// the color camera's lines of sight through the corners seen meet that plane. That's steadier
    /// than the picture alone, which has to judge the card's tilt from how squashed it looks, and
    /// is used only when there's no depth there.
    /// </summary>
    public static class CardLocator
    {
        /// <summary>The printed black square's side, in meters, for when only the picture is there to go on.</summary>
        public const float PrintedSize = 0.15f;
        private const int MinDepthPoints = 12;

        /// <summary>
        /// Where the card <paramref name="sighting"/> shows lies. Without depth around it this
        /// picture, <paramref name="lastPlane"/>, the plane it was last found on, stands in, so the
        /// card doesn't jump between the two ways of placing it; failing that, the picture alone
        /// is used, taking the card to be <paramref name="size"/> across (as measured before, if
        /// it was: prints come out a little bigger or smaller).
        /// </summary>
        public static CardPlacement Locate(CardSighting sighting, PinholeCamera camera, DepthFrame depth, int step = 2,
            Plane? lastPlane = null, float size = PrintedSize)
        {
            var corners = new Vector3[4];
            var fromDepth = false;
            var measuredPlane = depth == null ? null
                : HasTilt(depth) ? LevelPlane(sighting, camera, depth, step, Vector3.Normalize(depth.UpHint.Value))
                : DepthPlane(sighting, camera, depth, step);
            var plane = measuredPlane ?? lastPlane;
            if (plane != null)
            {
                fromDepth = measuredPlane != null;
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
                var fromPicture = FromPicture(sighting, camera, size);
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
            var measured = (right.Length() + toTop.Length()) / 2;
            if (measured < 0.03f || measured > 0.5f) return null;
            return new CardPlacement(sighting.Id, new TableFrame(middle, up, toTop), measured, fromDepth, plane);
        }

        /// <summary>The color camera's line of sight through <paramref name="pixel"/>, in camera space; one unit along it is one unit of its depth.</summary>
        public static Vector3 Ray(PinholeCamera camera, Vector2 pixel)
        {
            var y = (pixel.Y - camera.Cy) / camera.Fy;
            var x = (pixel.X - camera.Cx - camera.Skew * y) / camera.Fx;
            return camera.Forward + camera.Right * x + camera.Down * y;
        }

        /// <summary>
        /// The plane the card lies on, from the depth readings on the table in a ring around it,
        /// or null if there are too few or they aren't flat. Not from the card itself: the depth
        /// camera's infrared barely reflects off black ink, so its readings there are few and
        /// rough. The plane must pass through the card, so of the planes through the card's
        /// middle and the ring, the one most of the ring lies on is taken: a wall, an object or a
        /// hand nearby, or the floor past a table's edge, doesn't tip it.
        /// </summary>
        private static Plane? DepthPlane(CardSighting sighting, PinholeCamera camera, DepthFrame depth, int step)
        {
            var middle = CardMiddle(sighting, camera, depth, step);
            if (middle == null) return null;
            var ring = Collect(sighting, camera, depth, step, RingInner, RingOuter);
            if (ring.Count < MinDepthPoints) return null;

            // The plane through the middle and two ring readings that the most readings agree with.
            var random = new Random(1);
            Plane? best = null;
            var bestCount = 0;
            for (var i = 0; i < 60; i++)
            {
                var a = ring[random.Next(ring.Count)];
                var b = ring[random.Next(ring.Count)];
                var normal = Vector3.Cross(a - middle.Value, b - middle.Value);
                if (normal.LengthSquared() < 1e-8f) continue;
                normal = Vector3.Normalize(normal);
                var candidate = new Plane(normal, Vector3.Dot(normal, middle.Value));
                var count = 0;
                foreach (var p in ring) if (OnPlane(candidate, p)) count++;
                if (count <= bestCount) continue;
                bestCount = count;
                best = candidate;
            }
            if (best == null || bestCount < MinDepthPoints) return null;
            var kept = ring.FindAll(p => OnPlane(best.Value, p));
            var plane = PlaneFit.Fit(kept);
            // A plane fitted through the ring alone can sit a little off the card; it's flat on
            // the table, so that's fine, but it must not be far off.
            return Math.Abs(plane.SignedDistance(middle.Value)) < 0.02f ? plane : (Plane?)null;
        }

        private static bool OnPlane(Plane plane, Vector3 p) => Math.Abs(plane.SignedDistance(p)) < 0.006f + 0.003f * p.Z * p.Z;

        /// <summary>
        /// Where the card's middle is, from the depth readings on its white cells near the middle:
        /// the median of those, as some of the readings on the black ink are wild.
        /// </summary>
        private static Vector3? CardMiddle(CardSighting sighting, PinholeCamera camera, DepthFrame depth, int step)
        {
            var inside = Collect(sighting, camera, depth, step, 0f, 0.7f);
            if (inside.Count < 3) return null;
            inside.Sort((a, b) => a.Z.CompareTo(b.Z));
            var median = inside[inside.Count / 2];
            // The middle of the card along the line of sight through it, at that depth.
            var corners = sighting.Corners;
            var ray = Ray(camera, (corners[0] + corners[1] + corners[2] + corners[3]) / 4);
            var t = Vector3.Dot(median - camera.Position, camera.Forward) / Vector3.Dot(ray, camera.Forward);
            return camera.Position + ray * t;
        }

        private static bool HasTilt(DepthFrame depth) =>
            depth.UpHint is Vector3 up && Math.Abs(up.Length() - 1f) < 0.1f;

        /// <summary>
        /// The level plane the card lies on, with up as the Kinect's tilt sensor gives it, at the
        /// height of the table where the card is: the median height of the depth readings on the
        /// card and the table around it. A tilt sensor doesn't jitter, and seen at a shallow
        /// angle, the table's depth is rough enough that a plane fitted to a patch of it tilts a
        /// few degrees one way or the other from picture to picture; a median height doesn't.
        /// </summary>
        private static Plane? LevelPlane(CardSighting sighting, PinholeCamera camera, DepthFrame depth, int step, Vector3 up)
        {
            var points = Collect(sighting, camera, depth, step, 0f, RingOuter);
            if (points.Count < MinDepthPoints) return null;
            var heights = points.ConvertAll(p => Vector3.Dot(up, p));
            heights.Sort();
            var height = heights[heights.Count / 2];
            // Again from just the readings near that, so things nearby don't pull it.
            var near = heights.FindAll(h => Math.Abs(h - height) < 0.02f);
            if (near.Count < MinDepthPoints) return null;
            return new Plane(up, near[near.Count / 2]);
        }

        /// <summary>The ring around the card the table's plane is taken from, as multiples of the card's size about its middle.</summary>
        private const float RingInner = 1.3f;
        private const float RingOuter = 2.6f;

        /// <summary>
        /// The depth readings that show up in the color picture between <paramref name="inner"/>
        /// and <paramref name="outer"/> times the card's outline, scaled about its middle.
        /// </summary>
        private static List<Vector3> Collect(CardSighting sighting, PinholeCamera camera, DepthFrame depth, int step, float inner, float outer)
        {
            var middle = (sighting.Corners[0] + sighting.Corners[1] + sighting.Corners[2] + sighting.Corners[3]) / 4;
            var innerQuad = new Vector2[4];
            var outerQuad = new Vector2[4];
            for (var i = 0; i < 4; i++)
            {
                innerQuad[i] = middle + (sighting.Corners[i] - middle) * inner;
                outerQuad[i] = middle + (sighting.Corners[i] - middle) * outer;
            }
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var c in outerQuad)
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
                    if (pixel.X < minX || pixel.X > maxX || pixel.Y < minY || pixel.Y > maxY) continue;
                    if (!Inside(outerQuad, pixel) || (inner > 0f && Inside(innerQuad, pixel))) continue;
                    points.Add(p);
                }
            }
            return points;
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
        /// The card's corners from the picture alone, knowing its <paramref name="size"/>: the plane-to-picture
        /// mapping of its square, taken apart into how it's turned and where it is.
        /// </summary>
        private static Vector3[] FromPicture(CardSighting sighting, PinholeCamera camera, float size)
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
            var scale = 2f * size / (r1.Length() + r2.Length());
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

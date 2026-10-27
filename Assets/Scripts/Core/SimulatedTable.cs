using System;
using System.Collections.Generic;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>A box in the simulated room, from its lowest corner to its highest.</summary>
    public readonly struct Box
    {
        public Vector3 Min { get; }
        public Vector3 Max { get; }

        public Box(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        /// <summary>A box standing on the table, centered at (<paramref name="x"/>, <paramref name="z"/>).</summary>
        public static Box OnTable(float x, float z, float width, float depth, float height) =>
            new Box(new Vector3(x - width / 2, 0f, z - depth / 2), new Vector3(x + width / 2, height, z + depth / 2));
    }

    /// <summary>
    /// A stand-in for a Kinect: renders depth images of a simple room with a table, from a sensor
    /// at the table's near edge tilted down across it, the way the game expects it to be set up.
    /// Used by tests, and by the game when no Kinect is connected.
    ///
    /// The room's coordinates have the tabletop at Y = 0, X to the right as seen from the sensor and
    /// Z away from it, in meters. Depth comes out in Kinect camera space, like the real thing.
    /// </summary>
    public sealed class SimulatedTable
    {
        public float TableWidth = 1.2f;
        public float TableDepth = 0.75f;
        /// <summary>How far in front of the sensor the table's near edge is, along Z.</summary>
        public float TableNear = 0.25f;
        public float TableHeight = 0.75f;
        public float WallDistance = 1.8f;
        public Vector3 SensorPosition = new Vector3(0f, 0.55f, -0.1f);
        public float PitchDegrees = 45f;
        /// <summary>Depth noise at 1 m, in meters; it grows with the square of distance, as with a Kinect.</summary>
        public float Noise = 0.0015f;

        /// <summary>Things on the table.</summary>
        public readonly List<Box> Objects = new List<Box>();

        /// <summary>A hand over the table, as a sphere, or null.</summary>
        public Vector3? Hand;
        public float HandRadius = 0.05f;

        private long frameNumber;

        /// <summary>The Kinect v2's depth camera, scaled to the image size.</summary>
        private const float FocalAt512 = 365f;
        private const float MaxRange = 4.5f;

        private Vector3 SensorRight => new Vector3(1f, 0f, 0f);
        private Vector3 SensorForward
        {
            get
            {
                var pitch = PitchDegrees * Math.PI / 180.0;
                return new Vector3(0f, (float)-Math.Sin(pitch), (float)Math.Cos(pitch));
            }
        }
        private Vector3 SensorUp => Vector3.Cross(SensorForward, SensorRight);

        /// <summary>Where a room point appears in the sensor's camera space.</summary>
        public Vector3 RoomToCamera(Vector3 room)
        {
            var d = room - SensorPosition;
            // Kinect camera space has X toward the sensor's left.
            return new Vector3(-Vector3.Dot(d, SensorRight), Vector3.Dot(d, SensorUp), Vector3.Dot(d, SensorForward));
        }

        /// <summary>Renders one depth image of <paramref name="width"/> by <paramref name="height"/> pixels.</summary>
        public DepthFrame Render(int width = 256, int height = 212, Random random = null)
        {
            random = random ?? new Random();
            var frame = new DepthFrame(width, height) { Number = ++frameNumber };
            var focal = FocalAt512 * width / 512f;
            float cx = width / 2f, cy = height / 2f;
            var right = SensorRight;
            var up = SensorUp;
            var forward = SensorForward;
            var table = new Box(new Vector3(-TableWidth / 2, -TableHeight, TableNear),
                new Vector3(TableWidth / 2, 0f, TableNear + TableDepth));
            for (var py = 0; py < height; py++)
            {
                for (var px = 0; px < width; px++)
                {
                    // One unit along this direction is one unit of depth (camera Z).
                    var dir = forward + right * ((px - cx) / focal) + up * ((cy - py) / focal);
                    var t = Nearest(dir, table);
                    if (t > MaxRange || float.IsInfinity(t))
                    {
                        frame.Points[py * width + px] = DepthFrame.Invalid;
                        continue;
                    }
                    var z = t + Gaussian(random) * Noise * t * t;
                    var room = SensorPosition + dir * z;
                    frame.Points[py * width + px] = RoomToCamera(room);
                }
            }
            return frame;
        }

        /// <summary>Distance, in depth units along <paramref name="dir"/>, to the nearest thing it hits.</summary>
        private float Nearest(Vector3 dir, Box table)
        {
            var best = float.PositiveInfinity;
            var origin = SensorPosition;
            // The floor.
            if (dir.Y < 0f) best = Math.Min(best, (-TableHeight - origin.Y) / dir.Y);
            // The wall behind the table.
            if (dir.Z > 0f) best = Math.Min(best, (WallDistance - origin.Z) / dir.Z);
            best = Math.Min(best, HitBox(origin, dir, table));
            foreach (var box in Objects) best = Math.Min(best, HitBox(origin, dir, box));
            if (Hand != null) best = Math.Min(best, HitSphere(origin, dir, Hand.Value, HandRadius));
            return best;
        }

        private static float HitBox(Vector3 origin, Vector3 dir, Box box)
        {
            float near = 0f, far = float.PositiveInfinity;
            for (var axis = 0; axis < 3; axis++)
            {
                var o = Get(origin, axis);
                var d = Get(dir, axis);
                var lo = Get(box.Min, axis);
                var hi = Get(box.Max, axis);
                if (Math.Abs(d) < 1e-9f)
                {
                    if (o < lo || o > hi) return float.PositiveInfinity;
                    continue;
                }
                var t1 = (lo - o) / d;
                var t2 = (hi - o) / d;
                if (t1 > t2) { var swap = t1; t1 = t2; t2 = swap; }
                near = Math.Max(near, t1);
                far = Math.Min(far, t2);
                if (near > far) return float.PositiveInfinity;
            }
            return near > 0f ? near : float.PositiveInfinity;
        }

        private static float HitSphere(Vector3 origin, Vector3 dir, Vector3 center, float radius)
        {
            var oc = origin - center;
            var a = Vector3.Dot(dir, dir);
            var b = 2f * Vector3.Dot(oc, dir);
            var c = Vector3.Dot(oc, oc) - radius * radius;
            var discriminant = b * b - 4f * a * c;
            if (discriminant < 0f) return float.PositiveInfinity;
            var t = (-b - (float)Math.Sqrt(discriminant)) / (2f * a);
            return t > 0f ? t : float.PositiveInfinity;
        }

        private static float Get(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

        private static float Gaussian(Random random)
        {
            var u1 = 1.0 - random.NextDouble();
            var u2 = random.NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
        }
    }
}

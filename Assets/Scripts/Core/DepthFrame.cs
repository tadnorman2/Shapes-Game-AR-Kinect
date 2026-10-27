using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>
    /// One depth image as 3D points in the Kinect's camera space, in meters: X toward the sensor's
    /// left, Y up, Z straight out from the sensor. Pixels with no reading hold <see cref="Invalid"/>.
    /// </summary>
    public sealed class DepthFrame
    {
        public static readonly Vector3 Invalid = new Vector3(float.NaN, float.NaN, float.NaN);

        public int Width { get; }
        public int Height { get; }
        public Vector3[] Points { get; }

        /// <summary>Goes up with each new image, so readers can tell when one arrives.</summary>
        public long Number { get; set; }

        /// <summary>
        /// For each pixel, where it appears in the color camera's image, from 0 to 1 across and
        /// down, or NaN where it doesn't; null when there's no color camera.
        /// </summary>
        public Vector2[] ColorCoordinates { get; set; }

        public DepthFrame(int width, int height)
        {
            Width = width;
            Height = height;
            Points = new Vector3[width * height];
        }

        public Vector3 this[int x, int y] => Points[y * Width + x];

        public static bool IsValid(Vector3 point) => !float.IsNaN(point.Z) && point.Z > 0f;
    }
}

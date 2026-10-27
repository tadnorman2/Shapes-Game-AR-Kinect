using System;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>
    /// Coordinates on the table: X to the right as seen from the Kinect, Y up out of the table,
    /// Z away from the Kinect across the table, in meters, with the origin on the table's surface.
    /// This is also the game world's layout.
    /// </summary>
    public sealed class TableFrame
    {
        public Vector3 Origin { get; }
        public Vector3 Right { get; }
        public Vector3 Up { get; }
        public Vector3 Forward { get; }

        /// <summary>
        /// A frame on the plane through <paramref name="origin"/> with normal <paramref name="up"/>,
        /// both in camera space, facing the way the camera looks.
        /// </summary>
        public TableFrame(Vector3 origin, Vector3 up)
        {
            Origin = origin;
            Up = Vector3.Normalize(up);
            // The camera looks along +Z; flattened onto the table, that's "away across the table".
            var look = new Vector3(0f, 0f, 1f);
            var forward = look - Vector3.Dot(look, Up) * Up;
            if (forward.LengthSquared() < 1e-6f) forward = new Vector3(0f, 1f, 0f) - Vector3.Dot(new Vector3(0f, 1f, 0f), Up) * Up;
            Forward = Vector3.Normalize(forward);
            // Camera space has X to the sensor's left, so the viewer's right is Forward x Up.
            Right = Vector3.Normalize(Vector3.Cross(Forward, Up));
        }

        /// <summary>Converts a camera-space point to table coordinates.</summary>
        public Vector3 ToTable(Vector3 camera)
        {
            var d = camera - Origin;
            return new Vector3(Vector3.Dot(d, Right), Vector3.Dot(d, Up), Vector3.Dot(d, Forward));
        }

        /// <summary>Converts a point in table coordinates back to camera space.</summary>
        public Vector3 ToCamera(Vector3 table) => Origin + Right * table.X + Up * table.Y + Forward * table.Z;

        /// <summary>Converts a camera-space direction to table coordinates.</summary>
        public Vector3 DirectionToTable(Vector3 camera) =>
            new Vector3(Vector3.Dot(camera, Right), Vector3.Dot(camera, Up), Vector3.Dot(camera, Forward));

        /// <summary>Where the Kinect itself is, in table coordinates.</summary>
        public Vector3 CameraPosition => ToTable(Vector3.Zero);

        /// <summary>The direction the Kinect looks, in table coordinates.</summary>
        public Vector3 CameraLook => DirectionToTable(new Vector3(0f, 0f, 1f));

        /// <summary>The Kinect's up direction, in table coordinates.</summary>
        public Vector3 CameraUp => DirectionToTable(new Vector3(0f, 1f, 0f));

        public override string ToString() =>
            $"TableFrame(origin {Origin}, up {Up}, height of camera above table {Math.Round(CameraPosition.Y, 3)} m)";
    }
}

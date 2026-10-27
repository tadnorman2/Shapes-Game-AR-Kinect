using System.IO;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>
    /// Saves a depth image to a file and reads it back, so a picture the game had trouble with can
    /// be looked at, and tested against, away from the Kinect.
    /// </summary>
    public static class DepthCapture
    {
        private const int Magic = 0x31444B53; // "SKD1"

        public static void Save(DepthFrame frame, Stream stream)
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(Magic);
                writer.Write(frame.Width);
                writer.Write(frame.Height);
                writer.Write(frame.UpHint != null);
                var hint = frame.UpHint ?? Vector3.Zero;
                writer.Write(hint.X);
                writer.Write(hint.Y);
                writer.Write(hint.Z);
                foreach (var p in frame.Points)
                {
                    writer.Write(p.X);
                    writer.Write(p.Y);
                    writer.Write(p.Z);
                }
            }
        }

        public static DepthFrame Load(Stream stream)
        {
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                if (reader.ReadInt32() != Magic) throw new InvalidDataException("Not a depth capture.");
                var frame = new DepthFrame(reader.ReadInt32(), reader.ReadInt32());
                var hasHint = reader.ReadBoolean();
                var hint = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                if (hasHint) frame.UpHint = hint;
                for (var i = 0; i < frame.Points.Length; i++)
                {
                    frame.Points[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
                return frame;
            }
        }
    }
}

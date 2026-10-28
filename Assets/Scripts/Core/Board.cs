using System;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>
    /// The game's own arena, built around a card on the table and moving with it. Its
    /// <see cref="Frame"/> is the card's: the origin at the card's middle, X across, Z toward the
    /// card's printed top, and Y up out of the table.
    /// </summary>
    public sealed class Board
    {
        public ArenaLayout Layout { get; }
        public TableFrame Frame { get; private set; }
        public float Width => Layout.Width;
        public float Length => Layout.Length;

        public Board(ArenaLayout layout, TableFrame frame)
        {
            Layout = layout;
            Frame = frame;
        }

        /// <summary>Moves the board to follow its card.</summary>
        public void MoveTo(TableFrame frame) => Frame = frame;

        /// <summary>Whether (<paramref name="x"/>, <paramref name="z"/>), in the board's coordinates, is on it.</summary>
        public bool Contains(float x, float z) => Math.Abs(x) <= Width / 2 && Math.Abs(z) <= Length / 2;

        /// <summary>Whether the ball can be at (<paramref name="x"/>, <paramref name="z"/>): on the board and not in one of its walls.</summary>
        public bool IsOpen(float x, float z)
        {
            if (!Contains(x, z)) return false;
            foreach (var wall in Layout.Walls)
            {
                if (wall.Contains(x, z)) return false;
            }
            return true;
        }

        /// <summary>Calls <paramref name="segment"/> with each side of the board, in its own coordinates.</summary>
        public void TraceEdge(Action<float, float, float, float> segment)
        {
            float w = Width / 2, l = Length / 2;
            segment(-w, -l, w, -l);
            segment(w, -l, w, l);
            segment(w, l, -w, l);
            segment(-w, l, -w, -l);
        }

        /// <summary>How far apart two placements are: how far the origin moved, in meters, and how far it turned, in degrees.</summary>
        public static (float meters, float degrees) Difference(TableFrame a, TableFrame b)
        {
            var moved = Vector3.Distance(a.Origin, b.Origin);
            var cos = Math.Min(Vector3.Dot(a.Up, b.Up), Vector3.Dot(a.Forward, b.Forward));
            return (moved, (float)(Math.Acos(Math.Max(-1f, Math.Min(1f, cos))) * 180 / Math.PI));
        }
    }
}

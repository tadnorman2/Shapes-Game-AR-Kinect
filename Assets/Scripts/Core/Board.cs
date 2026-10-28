using System;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>
    /// The game's own arena: a rectangular board of a set size, put down on a surface the player
    /// picked, and moved and turned on it before play. Its <see cref="Frame"/> has the origin at
    /// the board's middle, X across it and Z along it, and Y up out of the surface.
    /// </summary>
    public sealed class Board
    {
        public PickedSurface Surface { get; }
        public float Width => DefaultWidth * Scale;
        public float Length => DefaultLength * Scale;
        /// <summary>The board's size, as a share of its full size.</summary>
        public float Scale { get; private set; } = 1f;
        /// <summary>Where the board's middle is on the surface, in the surface's coordinates.</summary>
        public float CenterX { get; private set; }
        public float CenterZ { get; private set; }
        /// <summary>How far the board is turned, in radians, from facing straight away from the Kinect.</summary>
        public float Turn { get; private set; }
        public TableFrame Frame { get; private set; }

        public const float DefaultWidth = 0.6f;
        public const float DefaultLength = 0.4f;
        public const float MinScale = 0.5f;
        /// <summary>How far the board may be slid from where the surface was picked, in meters.</summary>
        private const float MaxSlide = 1.5f;
        /// <summary>When fitting, a board with this much of it on the surface counts as fitting.</summary>
        private const float GoodFit = 0.95f;

        /// <summary>A full-size board on <paramref name="surface"/>, fitted on it near where it was picked (see <see cref="Fit"/>).</summary>
        public Board(PickedSurface surface)
        {
            Surface = surface;
            Place(0f, 0f, 0f);
            Fit();
        }

        /// <summary>Slides the board by (<paramref name="dx"/>, <paramref name="dz"/>) meters, in the surface's coordinates, and turns it by <paramref name="turn"/> radians.</summary>
        public void Move(float dx, float dz, float turn) =>
            Place(Clamp(CenterX + dx), Clamp(CenterZ + dz), Turn + turn);

        /// <summary>Makes the board bigger or smaller by <paramref name="factor"/>, between half and full size.</summary>
        public void Resize(float factor)
        {
            Scale = Math.Max(MinScale, Math.Min(1f, Scale * factor));
        }

        /// <summary>
        /// Puts the board where it best lies on the surface, near where the surface was picked:
        /// the biggest size, down to half, at which some spot and turn (straight or a quarter
        /// turn) has it on the surface, and of those spots, the nearest.
        /// </summary>
        public void Fit()
        {
            var grid = Surface.Grid;
            for (var scale = 1f; scale >= MinScale - 1e-3f; scale -= 0.1f)
            {
                var best = float.MaxValue;
                (float x, float z, float turn)? spot = null;
                for (var cell = 0; cell < grid.CellCount; cell += 1)
                {
                    // Every other cell each way is plenty of spots to try.
                    if (!Surface.IsSurface(cell) || (cell % grid.CellsPerSide) % 2 == 1 || (cell / grid.CellsPerSide) % 2 == 1) continue;
                    float x = grid.CenterX(cell), z = grid.CenterZ(cell);
                    var distance = x * x + z * z;
                    if (distance >= best || distance > MaxSlide * MaxSlide) continue;
                    foreach (var turn in new[] { 0f, (float)(Math.PI / 2) })
                    {
                        if (Coverage(x, z, turn, scale) < GoodFit) continue;
                        best = distance;
                        spot = (x, z, turn);
                        break;
                    }
                }
                if (spot == null) continue;
                Scale = scale;
                Place(spot.Value.x, spot.Value.z, spot.Value.turn);
                return;
            }
            Scale = MinScale;
            Place(0f, 0f, 0f);
        }

        private static float Clamp(float v) => Math.Max(-MaxSlide, Math.Min(MaxSlide, v));

        private void Place(float x, float z, float turn)
        {
            CenterX = x;
            CenterZ = z;
            Turn = turn;
            var surface = Surface.Frame;
            var along = surface.Forward * (float)Math.Cos(turn) + surface.Right * (float)Math.Sin(turn);
            Frame = new TableFrame(surface.ToCamera(new Vector3(x, 0f, z)), surface.Up, along);
        }

        /// <summary>Whether (<paramref name="x"/>, <paramref name="z"/>), in the board's coordinates, is on it.</summary>
        public bool Contains(float x, float z) => Math.Abs(x) <= Width / 2 && Math.Abs(z) <= Length / 2;

        /// <summary>How much of the board lies on the picked surface, from 0 to 1, as far as the Kinect can see it.</summary>
        public float Coverage() => Coverage(CenterX, CenterZ, Turn, Scale);

        /// <summary>How much of a board of <paramref name="scale"/> centered at (<paramref name="x"/>, <paramref name="z"/>) on the surface and turned by <paramref name="turn"/> would lie on it.</summary>
        private float Coverage(float x, float z, float turn, float scale)
        {
            const float step = 0.02f;
            float cos = (float)Math.Cos(turn), sin = (float)Math.Sin(turn);
            float halfWidth = DefaultWidth * scale / 2, halfLength = DefaultLength * scale / 2;
            int on = 0, all = 0;
            for (var bx = -halfWidth + step / 2; bx < halfWidth; bx += step)
            {
                for (var bz = -halfLength + step / 2; bz < halfLength; bz += step)
                {
                    all++;
                    // Turned as Place turns it: the board's Z runs along (sin, cos) on the
                    // surface, and its X along (cos, -sin).
                    var sx = x + bx * cos + bz * sin;
                    var sz = z - bx * sin + bz * cos;
                    if (Surface.Contains(sx, sz)) on++;
                }
            }
            return all == 0 ? 0f : on / (float)all;
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
    }
}

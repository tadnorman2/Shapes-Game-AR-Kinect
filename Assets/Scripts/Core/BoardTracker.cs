using System;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>
    /// Watches the board during play for real things put on it, like a cup or a book: what
    /// rises above the top of what was under each part of the board when play began. Comparing against how it looked, rather than against a perfectly flat plane, means
    /// a surface the Kinect measures a bit wrong, as it does at a shallow angle, doesn't show up
    /// as objects.
    /// </summary>
    public sealed class BoardTracker
    {
        public Board Board { get; }
        public ObstacleMap Obstacles { get; }
        /// <summary>Whether it has learned how the surface looks yet; until then nothing counts as an object.</summary>
        public bool IsReady => learned >= LearnFrames;

        /// <summary>The top of what's under each cell of the board, learned as play began.</summary>
        private readonly float[] baseline;
        private int learned;

        /// <summary>Pictures averaged to learn how the surface looks: a third of a second.</summary>
        private const int LearnFrames = 10;
        private const float MaxDistance = 4f;
        private const float MaxBelow = 0.05f;

        public BoardTracker(Board board)
        {
            Board = board;
            var grid = new Grid(Math.Max(board.Width, board.Length) + 0.1f, 0.02f);
            Obstacles = new ObstacleMap(grid);
            baseline = new float[grid.CellCount];
            for (var i = 0; i < baseline.Length; i++) baseline[i] = float.NaN;
        }

        /// <summary>Takes in a depth picture, using every <paramref name="step"/>th pixel each way.</summary>
        public void Update(DepthFrame depth, int step = 1)
        {
            if (!IsReady)
            {
                ForEachOnBoard(depth, step, (cell, t, z) =>
                {
                    if (float.IsNaN(baseline[cell]) || t.Y > baseline[cell]) baseline[cell] = t.Y;
                });
                if (++learned == LearnFrames)
                {
                    for (var i = 0; i < baseline.Length; i++) if (float.IsNaN(baseline[i])) baseline[i] = 0f;
                }
                return;
            }
            Obstacles.BeginFrame();
            ForEachOnBoard(depth, step, (cell, t, z) =>
            {
                var above = t.Y - baseline[cell];
                // Allow for the Kinect's depth getting rougher with distance.
                if (above > 0f) above = Math.Max(0f, above - Noise(z) * 2f);
                Obstacles.Observe(t.X, t.Z, above);
            });
            Obstacles.EndFrame();
        }

        /// <summary>Where the ball can go: the board, minus the things on it.</summary>
        public IPlayArea PlayArea() => new PlayArea((x, z) => Board.Contains(x, z) && !Obstacles.IsBlocked(x, z));

        private void ForEachOnBoard(DepthFrame depth, int step, Action<int, Vector3, float> point)
        {
            var frame = Board.Frame;
            var grid = Obstacles.Grid;
            for (var y = 0; y < depth.Height; y += step)
            {
                for (var x = 0; x < depth.Width; x += step)
                {
                    var p = depth[x, y];
                    if (!DepthFrame.IsValid(p) || p.Z > MaxDistance) continue;
                    var t = frame.ToTable(p);
                    // Well below the board is the floor past a table's edge, or its side: not on it.
                    if (!Board.Contains(t.X, t.Z) || t.Y < -MaxBelow) continue;
                    var cell = grid.CellAt(t.X, t.Z);
                    if (cell >= 0) point(cell, t, p.Z);
                }
            }
        }

        /// <summary>Roughly how far the Kinect's depth wanders at <paramref name="z"/> meters, in meters.</summary>
        private static float Noise(float z) => 0.002f + 0.002f * z * z;
    }
}

using System;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>
    /// Watches the board for real things put on it, like a cup or a book: what rises above the
    /// top of what was under each part of the board when it settled where it is. Comparing
    /// against how it looked, rather than against a perfectly flat plane, means a surface the
    /// Kinect measures a bit wrong, as it does at a shallow angle, doesn't show up as objects.
    /// When the board moves with its card, it looks afresh.
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
        /// <summary>Where the board was when it began learning how the surface looks.</summary>
        private TableFrame learnedAt;

        /// <summary>Pictures to learn how the surface looks from: half a second.</summary>
        private const int LearnFrames = 15;
        private const float MaxDistance = 4f;
        private const float MaxBelow = 0.05f;
        /// <summary>A board moved more than this far, or turned more than this many degrees, looks afresh.</summary>
        private const float MaxMove = 0.015f;
        private const float MaxTurn = 3f;

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
            if (learnedAt == null || Moved()) Restart();
            if (!IsReady)
            {
                ForEachOnBoard(depth, step, (cell, t, z) =>
                {
                    if (float.IsNaN(baseline[cell]) || t.Y > baseline[cell]) baseline[cell] = t.Y;
                });
                if (++learned == LearnFrames) SpreadBaseline();
                return;
            }
            Obstacles.BeginFrame();
            ForEachOnBoard(depth, step, (cell, t, z) =>
            {
                // A cell that had no readings while learning: learn it now, rather than guess.
                if (float.IsNaN(baseline[cell])) baseline[cell] = t.Y;
                var above = t.Y - baseline[cell];
                // Allow for the Kinect's depth getting rougher with distance.
                if (above > 0f) above = Math.Max(0f, above - Noise(z) * 2f);
                Obstacles.Observe(t.X, t.Z, above);
            });
            Obstacles.EndFrame();
        }

        /// <summary>
        /// Makes each cell's top the highest of it and its neighbors. Readings shift a little from
        /// picture to picture, and one landing in the next cell over mustn't look like a bump
        /// where the surface slopes, as a table seen at a shallow angle seems to.
        /// </summary>
        private void SpreadBaseline()
        {
            var n = Obstacles.Grid.CellsPerSide;
            var spread = new float[baseline.Length];
            for (var i = 0; i < baseline.Length; i++)
            {
                int row = i / n, column = i % n;
                var top = float.NaN;
                for (var dr = -1; dr <= 1; dr++)
                {
                    for (var dc = -1; dc <= 1; dc++)
                    {
                        int r = row + dr, c = column + dc;
                        if (r < 0 || r >= n || c < 0 || c >= n) continue;
                        var h = baseline[r * n + c];
                        if (!float.IsNaN(h) && (float.IsNaN(top) || h > top)) top = h;
                    }
                }
                spread[i] = top;
            }
            Array.Copy(spread, baseline, baseline.Length);
        }

        /// <summary>Where the ball can go: the board, minus its walls and the things on it.</summary>
        public IPlayArea PlayArea() => new PlayArea((x, z) => Board.IsOpen(x, z) && (!IsReady || !Obstacles.IsBlocked(x, z)));

        private bool Moved()
        {
            var (meters, degrees) = Board.Difference(learnedAt, Board.Frame);
            return meters > MaxMove || degrees > MaxTurn;
        }

        /// <summary>Starts learning how the surface under the board looks, forgetting what was on it.</summary>
        private void Restart()
        {
            learnedAt = Board.Frame;
            learned = 0;
            for (var i = 0; i < baseline.Length; i++) baseline[i] = float.NaN;
            Obstacles.Clear();
        }

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

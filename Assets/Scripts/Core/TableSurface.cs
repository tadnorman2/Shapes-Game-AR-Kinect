using System;
using System.Collections.Generic;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>
    /// The table's playable surface: where the Kinect saw flat table when the table was found.
    /// Found once, with the table clear, so later objects and hands don't change it.
    /// </summary>
    public sealed class TableSurface : IPlayArea
    {
        public TableFrame Frame { get; }
        public Grid Grid { get; }
        private readonly bool[] cells;

        private TableSurface(TableFrame frame, Grid grid, bool[] cells)
        {
            Frame = frame;
            Grid = grid;
            this.cells = cells;
        }

        public bool Contains(float x, float z)
        {
            var cell = Grid.CellAt(x, z);
            return cell >= 0 && cells[cell];
        }

        public bool IsSurface(int cell) => cells[cell];

        /// <summary>The surface's area in square meters.</summary>
        public float Area
        {
            get
            {
                var count = 0;
                foreach (var c in cells) if (c) count++;
                return count * Grid.CellSize * Grid.CellSize;
            }
        }

        /// <summary>Calls <paramref name="segment"/> with each piece of the line around the surface's edge.</summary>
        public void TraceEdge(Action<float, float, float, float> segment) => Outline.Trace(Grid, IsSurface, segment);

        /// <summary>
        /// Finds the table in <paramref name="depth"/> (see <see cref="PlaneFit.FindTable"/>), trimmed
        /// to the biggest connected piece of it. Returns null if there's no
        /// such surface, or it's too small to play on.
        /// </summary>
        public static TableSurface Find(DepthFrame depth, float cellSize = 0.02f, float gridSize = 4f)
        {
            var samples = new List<Vector3>();
            var step = Math.Max(1, depth.Width / 128);
            for (var y = 0; y < depth.Height; y += step)
            {
                for (var x = 0; x < depth.Width; x += step)
                {
                    var p = depth[x, y];
                    if (DepthFrame.IsValid(p) && p.Z < MaxDistance) samples.Add(p);
                }
            }
            var plane = PlaneFit.FindTable(samples, new Vector3(0f, 1f, 0f));
            if (plane == null) return null;

            // Mark every cell the plane's points land in, using the full image.
            var p0 = plane.Value.Normal * plane.Value.Distance;
            var roughFrame = new TableFrame(p0, plane.Value.Normal);
            var grid = new Grid(gridSize, cellSize);
            var hits = new int[grid.CellCount];
            foreach (var point in depth.Points)
            {
                if (!DepthFrame.IsValid(point) || point.Z > MaxDistance) continue;
                if (Math.Abs(plane.Value.SignedDistance(point)) > PlaneFit.InlierDistance) continue;
                var t = roughFrame.ToTable(point);
                var cell = grid.CellAt(t.X, t.Z);
                if (cell >= 0) hits[cell]++;
            }
            var marked = new bool[grid.CellCount];
            for (var i = 0; i < marked.Length; i++) marked[i] = hits[i] > 0;

            // Close small gaps where no depth landed, then keep the biggest connected piece.
            marked = Close(grid, marked);
            var piece = BiggestPiece(grid, marked);
            if (piece == null) return null;

            // Re-center the frame on the piece, so the table's middle is the origin.
            float sumX = 0, sumZ = 0;
            var count = 0;
            for (var i = 0; i < piece.Length; i++)
            {
                if (!piece[i]) continue;
                sumX += grid.CenterX(i);
                sumZ += grid.CenterZ(i);
                count++;
            }
            if (count * cellSize * cellSize < MinArea) return null;
            var middle = p0 + roughFrame.Right * (sumX / count) + roughFrame.Forward * (sumZ / count);
            var frame = new TableFrame(middle, plane.Value.Normal);
            var cells = new bool[grid.CellCount];
            for (var i = 0; i < piece.Length; i++)
            {
                if (!piece[i]) continue;
                var cell = grid.CellAt(grid.CenterX(i) - sumX / count, grid.CenterZ(i) - sumZ / count);
                if (cell >= 0) cells[cell] = true;
            }
            return new TableSurface(frame, grid, cells);
        }

        /// <summary>Fills gaps up to one cell wide (dilate, then erode).</summary>
        private static bool[] Close(Grid grid, bool[] cells)
        {
            var n = grid.CellsPerSide;
            bool Any(bool[] source, int i)
            {
                int row = i / n, column = i % n;
                for (var dr = -1; dr <= 1; dr++)
                {
                    for (var dc = -1; dc <= 1; dc++)
                    {
                        int r = row + dr, c = column + dc;
                        if (r >= 0 && r < n && c >= 0 && c < n && source[r * n + c]) return true;
                    }
                }
                return false;
            }
            bool All(bool[] source, int i)
            {
                int row = i / n, column = i % n;
                for (var dr = -1; dr <= 1; dr++)
                {
                    for (var dc = -1; dc <= 1; dc++)
                    {
                        int r = row + dr, c = column + dc;
                        if (r < 0 || r >= n || c < 0 || c >= n || !source[r * n + c]) return false;
                    }
                }
                return true;
            }
            var grown = new bool[cells.Length];
            for (var i = 0; i < cells.Length; i++) grown[i] = Any(cells, i);
            var closed = new bool[cells.Length];
            for (var i = 0; i < cells.Length; i++) closed[i] = All(grown, i);
            return closed;
        }

        /// <summary>The biggest 4-connected group of marked cells, or null if none are marked.</summary>
        private static bool[] BiggestPiece(Grid grid, bool[] cells)
        {
            var n = grid.CellsPerSide;
            var label = new int[cells.Length];
            var queue = new int[cells.Length];
            int bestLabel = 0, bestSize = 0, next = 0;
            for (var start = 0; start < cells.Length; start++)
            {
                if (!cells[start] || label[start] != 0) continue;
                next++;
                int head = 0, tail = 0;
                queue[tail++] = start;
                label[start] = next;
                while (head < tail)
                {
                    var i = queue[head++];
                    int row = i / n, column = i % n;
                    void Visit(int r, int c)
                    {
                        if (r < 0 || r >= n || c < 0 || c >= n) return;
                        var j = r * n + c;
                        if (!cells[j] || label[j] != 0) return;
                        label[j] = next;
                        queue[tail++] = j;
                    }
                    Visit(row - 1, column);
                    Visit(row + 1, column);
                    Visit(row, column - 1);
                    Visit(row, column + 1);
                }
                if (tail > bestSize)
                {
                    bestSize = tail;
                    bestLabel = next;
                }
            }
            if (bestLabel == 0) return null;
            var piece = new bool[cells.Length];
            for (var i = 0; i < cells.Length; i++) piece[i] = label[i] == bestLabel;
            return piece;
        }

        /// <summary>Depth past this is too rough to judge a surface by, in meters.</summary>
        private const float MaxDistance = 3.5f;

        /// <summary>About 30 by 30 cm: anything smaller leaves no room to play.</summary>
        private const float MinArea = 0.09f;
    }
}

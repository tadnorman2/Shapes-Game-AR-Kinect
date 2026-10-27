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
        /// Finds the table in <paramref name="depth"/> (see <see cref="TableFinder"/>), trimmed to the
        /// biggest connected piece of it. Returns null if there's no such surface, or it's too
        /// small to play on. <paramref name="report"/>, if given, gets what was seen, for the log.
        /// </summary>
        public static TableSurface Find(DepthFrame depth, List<string> report = null)
        {
            var table = TableFinder.Find(depth, report: report);
            if (table == null) return null;
            var piece = TableFinder.Piece(depth, table.Plane, out var middle);
            if (piece == null || piece.Area < TableFinder.MinArea) return null;

            // Re-center the grid on the table's middle, so that's the origin.
            var frame = new TableFrame(middle, table.Plane.Normal);
            var grid = piece.Grid;
            var cells = new bool[grid.CellCount];
            for (var i = 0; i < piece.Cells.Length; i++)
            {
                if (!piece.Cells[i]) continue;
                var t = frame.ToTable(piece.Frame.ToCamera(new Vector3(grid.CenterX(i), 0f, grid.CenterZ(i))));
                var cell = grid.CellAt(t.X, t.Z);
                if (cell >= 0) cells[cell] = true;
            }
            return new TableSurface(frame, grid, cells);
        }
    }
}

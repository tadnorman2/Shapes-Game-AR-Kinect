using System;

namespace ShapesKinect.Core
{
    /// <summary>
    /// A square grid of cells lying on the table, <see cref="Size"/> meters across and centered on
    /// the table's origin, indexing cells by table X and Z.
    /// </summary>
    public class Grid
    {
        public float Size { get; }
        public float CellSize { get; }
        public int CellsPerSide { get; }

        public Grid(float size, float cellSize)
        {
            Size = size;
            CellSize = cellSize;
            CellsPerSide = (int)Math.Round(size / cellSize);
        }

        public int CellCount => CellsPerSide * CellsPerSide;

        /// <summary>The cell at table position (x, z), or -1 off the grid.</summary>
        public int CellAt(float x, float z)
        {
            var column = (int)Math.Floor((x + Size / 2) / CellSize);
            var row = (int)Math.Floor((z + Size / 2) / CellSize);
            if (column < 0 || column >= CellsPerSide || row < 0 || row >= CellsPerSide) return -1;
            return row * CellsPerSide + column;
        }

        public float CenterX(int cell) => (cell % CellsPerSide + 0.5f) * CellSize - Size / 2;
        public float CenterZ(int cell) => (cell / CellsPerSide + 0.5f) * CellSize - Size / 2;
    }
}

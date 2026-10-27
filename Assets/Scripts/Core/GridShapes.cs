namespace ShapesKinect.Core
{
    /// <summary>Clean-up for shapes marked on a <see cref="Grid"/>.</summary>
    public static class GridShapes
    {
        /// <summary>Fills gaps up to one cell wide (dilate, then erode).</summary>
        public static bool[] Close(Grid grid, bool[] cells)
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
        public static bool[] BiggestPiece(Grid grid, bool[] cells)
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
    }
}

using System;

namespace ShapesKinect.Core
{
    /// <summary>
    /// A live top-down map of real things standing on the table, like a cup, a book or a box,
    /// built from depth points in table coordinates (Y is height above the table).
    ///
    /// Only points between <see cref="MinHeight"/> and <see cref="MaxHeight"/> above the table
    /// count, so things touching the table show up, but a hand or arm hovering over it doesn't.
    /// A cell has to look raised in three frames running to count, and only groups of a few
    /// cells count, as a real object covers several, while the Kinect's depth can throw up a stray
    /// bump for a frame or two. Feed each frame's points between <see cref="BeginFrame"/> and
    /// <see cref="EndFrame"/>; cells a frame doesn't see, like the far side of an object, keep their state.
    /// </summary>
    public sealed class ObstacleMap
    {
        public const float MinHeight = 0.015f;
        public const float MaxHeight = 0.15f;
        private const float SurfaceTolerance = 0.008f;
        private const int MinPoints = 2;
        private const int BlockedScore = 6;
        private const int MaxScore = 10;
        /// <summary>Raised cells in groups smaller than this don't count: about 2 by 3 cm.</summary>
        public const int MinCells = 6;

        public Grid Grid { get; }
        private readonly sbyte[] scores;
        private readonly int[] raised;
        private readonly int[] flat;
        private readonly int[] touched;
        private int touchedCount;
        /// <summary>Cells that count as part of an object: raised, in a big enough group.</summary>
        private readonly bool[] solid;
        private readonly int[] queue;
        private bool changed;

        /// <summary>Goes up whenever a cell starts or stops counting as an object, so drawing can rebuild.</summary>
        public int Version { get; private set; }

        public ObstacleMap(Grid grid)
        {
            Grid = grid;
            scores = new sbyte[grid.CellCount];
            raised = new int[grid.CellCount];
            flat = new int[grid.CellCount];
            touched = new int[grid.CellCount];
            solid = new bool[grid.CellCount];
            queue = new int[grid.CellCount];
        }

        public void BeginFrame() => touchedCount = 0;

        /// <summary>Records one real point at table position (x, z), <paramref name="height"/> meters above it.</summary>
        public void Observe(float x, float z, float height)
        {
            var cell = Grid.CellAt(x, z);
            if (cell < 0) return;
            var isRaised = height >= MinHeight && height <= MaxHeight;
            var isFlat = Math.Abs(height) <= SurfaceTolerance;
            if (!isRaised && !isFlat) return;
            if (raised[cell] == 0 && flat[cell] == 0) touched[touchedCount++] = cell;
            if (isRaised) raised[cell]++;
            else flat[cell]++;
        }

        public void EndFrame()
        {
            for (var i = 0; i < touchedCount; i++)
            {
                var cell = touched[i];
                int before = scores[cell], after = before;
                if (raised[cell] >= MinPoints && raised[cell] >= flat[cell]) after = Math.Min(before + 2, MaxScore);
                else if (flat[cell] > 0) after = Math.Max(before - 2, 0);
                scores[cell] = (sbyte)after;
                if ((before >= BlockedScore) != (after >= BlockedScore)) changed = true;
                raised[cell] = 0;
                flat[cell] = 0;
            }
            touchedCount = 0;
            if (!changed) return;
            changed = false;
            FindSolid();
            Version++;
        }

        /// <summary>Marks the raised cells that are in groups of at least <see cref="MinCells"/>.</summary>
        private void FindSolid()
        {
            Array.Clear(solid, 0, solid.Length);
            var n = Grid.CellsPerSide;
            var seen = new bool[scores.Length];
            for (var start = 0; start < scores.Length; start++)
            {
                if (scores[start] < BlockedScore || seen[start]) continue;
                int head = 0, tail = 0;
                queue[tail++] = start;
                seen[start] = true;
                while (head < tail)
                {
                    var i = queue[head++];
                    int row = i / n, column = i % n;
                    for (var dr = -1; dr <= 1; dr++)
                    {
                        for (var dc = -1; dc <= 1; dc++)
                        {
                            int r = row + dr, c = column + dc;
                            if (r < 0 || r >= n || c < 0 || c >= n) continue;
                            var j = r * n + c;
                            if (seen[j] || scores[j] < BlockedScore) continue;
                            seen[j] = true;
                            queue[tail++] = j;
                        }
                    }
                }
                if (tail < MinCells) continue;
                for (var k = 0; k < tail; k++) solid[queue[k]] = true;
            }
        }

        public bool IsBlocked(float x, float z)
        {
            var cell = Grid.CellAt(x, z);
            return cell >= 0 && IsBlocked(cell);
        }

        public bool IsBlocked(int cell) => solid[cell];

        /// <summary>Forgets everything, e.g. when the table is found again.</summary>
        public void Clear()
        {
            Array.Clear(scores, 0, scores.Length);
            Array.Clear(solid, 0, solid.Length);
            Version++;
        }

        /// <summary>Calls <paramref name="segment"/> with each piece of a ring around every object's base.</summary>
        public void TraceOutlines(Action<float, float, float, float> segment) => Outline.Trace(Grid, IsBlocked, segment);
    }
}

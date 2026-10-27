using System;

namespace ShapesKinect.Core
{
    /// <summary>
    /// A live top-down map of real things standing on the table, like a cup, a book or a box,
    /// built from depth points in table coordinates (Y is height above the table).
    ///
    /// Only points between <see cref="MinHeight"/> and <see cref="MaxHeight"/> above the table
    /// count, so things touching the table show up, but a hand or arm hovering over it doesn't.
    /// The Kinect's depth is steady enough that a couple of frames of agreement settle a cell.
    /// Feed each frame's points between <see cref="BeginFrame"/> and <see cref="EndFrame"/>; cells
    /// a frame doesn't see, like the far side of an object, keep their state.
    /// </summary>
    public sealed class ObstacleMap
    {
        public const float MinHeight = 0.015f;
        public const float MaxHeight = 0.15f;
        private const float SurfaceTolerance = 0.008f;
        private const int MinPoints = 2;
        private const int BlockedScore = 4;
        private const int MaxScore = 8;

        public Grid Grid { get; }
        private readonly sbyte[] scores;
        private readonly int[] raised;
        private readonly int[] flat;
        private readonly int[] touched;
        private int touchedCount;

        /// <summary>Goes up whenever a cell starts or stops counting as an object, so drawing can rebuild.</summary>
        public int Version { get; private set; }

        public ObstacleMap(Grid grid)
        {
            Grid = grid;
            scores = new sbyte[grid.CellCount];
            raised = new int[grid.CellCount];
            flat = new int[grid.CellCount];
            touched = new int[grid.CellCount];
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
                if ((before >= BlockedScore) != (after >= BlockedScore)) Version++;
                raised[cell] = 0;
                flat[cell] = 0;
            }
            touchedCount = 0;
        }

        public bool IsBlocked(float x, float z)
        {
            var cell = Grid.CellAt(x, z);
            return cell >= 0 && IsBlocked(cell);
        }

        public bool IsBlocked(int cell) => scores[cell] >= BlockedScore;

        /// <summary>Forgets everything, e.g. when the table is found again.</summary>
        public void Clear()
        {
            Array.Clear(scores, 0, scores.Length);
            Version++;
        }

        /// <summary>Calls <paramref name="segment"/> with each piece of a ring around every object's base.</summary>
        public void TraceOutlines(Action<float, float, float, float> segment) => Outline.Trace(Grid, IsBlocked, segment);
    }
}

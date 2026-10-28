using System;
using System.Collections.Generic;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>
    /// The surface the game is played on, exactly as the Kinect measures it: a top-down map of
    /// heights on a grid, with which cells make up the arena. Nothing assumes the surface is flat.
    /// Where the Kinect gets distances a little wrong, as it does looking across a table at a
    /// shallow angle, the map is wrong the same way, and anything drawn on it still lands on the
    /// right spot in the color picture: the error is only along the Kinect's line of sight.
    ///
    /// Coordinates are <see cref="Frame"/>'s: level, with Y up against gravity, and the Kinect at
    /// height 0, so surfaces below it have negative heights.
    /// </summary>
    public sealed class SurfaceMap : IPlayArea
    {
        public TableFrame Frame { get; }
        public Grid Grid { get; }
        private readonly float[] heights;
        private readonly bool[] arena;

        /// <summary>Where a round starts: in the arena, near its middle.</summary>
        public float StartX { get; }
        public float StartZ { get; }
        /// <summary>The arena's average height.</summary>
        public float MeanHeight { get; }

        /// <summary>About 30 by 30 cm: anything smaller leaves no room to play.</summary>
        public const float MinArea = 0.09f;

        private SurfaceMap(TableFrame frame, Grid grid, float[] heights, bool[] arena)
        {
            Frame = frame;
            Grid = grid;
            this.heights = heights;
            this.arena = arena;
            double sumX = 0, sumZ = 0, sumHeight = 0;
            var count = 0;
            for (var i = 0; i < arena.Length; i++)
            {
                if (!arena[i]) continue;
                sumX += grid.CenterX(i);
                sumZ += grid.CenterZ(i);
                sumHeight += heights[i];
                count++;
            }
            MeanHeight = (float)(sumHeight / count);
            // The arena cell nearest its middle, in case the middle itself isn't in it.
            float middleX = (float)(sumX / count), middleZ = (float)(sumZ / count);
            var best = float.MaxValue;
            for (var i = 0; i < arena.Length; i++)
            {
                if (!arena[i]) continue;
                var d = (grid.CenterX(i) - middleX) * (grid.CenterX(i) - middleX) + (grid.CenterZ(i) - middleZ) * (grid.CenterZ(i) - middleZ);
                if (d >= best) continue;
                best = d;
                StartX = grid.CenterX(i);
                StartZ = grid.CenterZ(i);
            }
        }

        public bool Contains(float x, float z)
        {
            var cell = Grid.CellAt(x, z);
            return cell >= 0 && arena[cell];
        }

        public bool IsArena(int cell) => arena[cell];

        /// <summary>The surface's measured height in <paramref name="cell"/>, or NaN where it wasn't seen.</summary>
        public float CellHeight(int cell) => heights[cell];

        /// <summary>The arena's area in square meters.</summary>
        public float Area
        {
            get
            {
                var count = 0;
                foreach (var a in arena) if (a) count++;
                return count * Grid.CellSize * Grid.CellSize;
            }
        }

        /// <summary>How far the arena reaches side to side (X) and away from the Kinect (Z), in meters.</summary>
        public (float width, float depth) Extent
        {
            get
            {
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                for (var i = 0; i < arena.Length; i++)
                {
                    if (!arena[i]) continue;
                    minX = Math.Min(minX, Grid.CenterX(i));
                    maxX = Math.Max(maxX, Grid.CenterX(i));
                    minZ = Math.Min(minZ, Grid.CenterZ(i));
                    maxZ = Math.Max(maxZ, Grid.CenterZ(i));
                }
                return (maxX - minX + Grid.CellSize, maxZ - minZ + Grid.CellSize);
            }
        }

        /// <summary>
        /// The arena's height at (<paramref name="x"/>, <paramref name="z"/>), blended smoothly
        /// between cells so things moving across it don't jump. Only the arena's own cells count,
        /// so near its edge the height doesn't get pulled up by an object or down off the edge;
        /// off the arena, it's the nearest arena cell's.
        /// </summary>
        public float HeightAt(float x, float z)
        {
            var n = Grid.CellsPerSide;
            var fx = (x + Grid.Size / 2) / Grid.CellSize - 0.5f;
            var fz = (z + Grid.Size / 2) / Grid.CellSize - 0.5f;
            var column = (int)Math.Floor(fx);
            var row = (int)Math.Floor(fz);
            float tx = fx - column, tz = fz - row;
            float sum = 0, weights = 0;
            for (var dr = 0; dr <= 1; dr++)
            {
                for (var dc = 0; dc <= 1; dc++)
                {
                    int r = row + dr, c = column + dc;
                    if (r < 0 || r >= n || c < 0 || c >= n || !arena[r * n + c]) continue;
                    var w = (dc == 1 ? tx : 1 - tx) * (dr == 1 ? tz : 1 - tz) + 1e-4f;
                    sum += heights[r * n + c] * w;
                    weights += w;
                }
            }
            if (weights > 0) return sum / weights;
            // Off the arena: the nearest arena cell within a few cells.
            var best = float.MaxValue;
            var height = MeanHeight;
            for (var dr = -3; dr <= 3; dr++)
            {
                for (var dc = -3; dc <= 3; dc++)
                {
                    int r = (int)Math.Round(fz) + dr, c = (int)Math.Round(fx) + dc;
                    if (r < 0 || r >= n || c < 0 || c >= n || !arena[r * n + c]) continue;
                    var d = (r - fz) * (r - fz) + (c - fx) * (c - fx);
                    if (d >= best) continue;
                    best = d;
                    height = heights[r * n + c];
                }
            }
            return height;
        }

        /// <summary>A point on the surface at (<paramref name="x"/>, <paramref name="z"/>), in <see cref="Frame"/>'s coordinates.</summary>
        public Vector3 OnSurface(float x, float z) => new Vector3(x, HeightAt(x, z), z);

        /// <summary>Calls <paramref name="segment"/> with each piece of the line around the arena's edge.</summary>
        public void TraceEdge(Action<float, float, float, float> segment) => Outline.Trace(Grid, IsArena, segment);

        /// <summary>
        /// The arena from the cells found to be the play surface (<paramref name="surface"/>), with
        /// their measured <paramref name="heights"/>: smoothed of ragged single-cell bites and
        /// kept to one piece. Returns null if what's left is too small to play on.
        /// </summary>
        public static SurfaceMap Build(TableFrame frame, Grid grid, float[] heights, bool[] surface)
        {
            var arena = GridShapes.BiggestPiece(grid, GridShapes.Close(grid, surface));
            if (arena == null) return null;
            heights = (float[])heights.Clone();
            var count = 0;
            for (var i = 0; i < arena.Length; i++)
            {
                if (!arena[i]) continue;
                count++;
                // Closing can take in a cell with no reading; give it its neighbors' height.
                if (float.IsNaN(heights[i])) heights[i] = NeighborAverage(grid, heights, i) ?? float.NaN;
            }
            if (count * grid.CellSize * grid.CellSize < MinArea) return null;
            for (var i = 0; i < arena.Length; i++)
            {
                if (arena[i] && float.IsNaN(heights[i])) arena[i] = false;
            }
            return new SurfaceMap(frame, grid, heights, arena);
        }

        /// <summary>The average height of a cell's eight neighbors that have one, if at least <paramref name="minNeighbors"/> do.</summary>
        private static float? NeighborAverage(Grid grid, float[] heights, int cell, int minNeighbors = 1)
        {
            var n = grid.CellsPerSide;
            int row = cell / n, column = cell % n;
            float sum = 0;
            var count = 0;
            for (var dr = -1; dr <= 1; dr++)
            {
                for (var dc = -1; dc <= 1; dc++)
                {
                    int r = row + dr, c = column + dc;
                    if ((dr == 0 && dc == 0) || r < 0 || r >= n || c < 0 || c >= n) continue;
                    var h = heights[r * n + c];
                    if (float.IsNaN(h)) continue;
                    sum += h;
                    count++;
                }
            }
            return count >= minNeighbors ? sum / count : (float?)null;
        }
    }
}

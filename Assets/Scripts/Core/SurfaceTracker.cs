using System;
using System.Collections.Generic;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>
    /// Follows the surface the Kinect sees, with no setup step. Between rounds it keeps
    /// measuring and picks the arena afresh every few frames (<see cref="Map"/>), so pointing the
    /// Kinect somewhere new just moves the arena there. During a round (<see cref="Freeze"/>) the
    /// arena stays put, and anything that rises from it, like a cup or a book, shows up in
    /// <see cref="Obstacles"/>.
    ///
    /// Heights are measured level, with up being the way the level surfaces in view face (see
    /// <see cref="UpFinder"/>), so the game's idea of flat matches the Kinect's depth.
    /// </summary>
    public sealed class SurfaceTracker
    {
        /// <summary>The arena, or null while there's no surface big enough in view.</summary>
        public SurfaceMap Map { get; private set; }
        /// <summary>Objects on the arena, during a round.</summary>
        public ObstacleMap Obstacles { get; private set; }
        /// <summary>Whether a round has the arena held still.</summary>
        public bool IsFrozen { get; private set; }
        /// <summary>Goes up whenever <see cref="Map"/> changes, so drawing can rebuild.</summary>
        public int Version { get; private set; }
        /// <summary>Level coordinates for the Kinect's current view, or null before the first picture.</summary>
        public TableFrame Frame { get; private set; }

        private readonly Grid grid = new Grid(GridSize, 0.02f);
        /// <summary>Each cell's measured height, blended over recent pictures; NaN if never on the surface.</summary>
        private readonly float[] reference;
        /// <summary>How sure each cell is part of the play surface, from 0 to 1, over recent pictures.</summary>
        private readonly float[] score;
        private readonly int[] lastSeen;
        // Scratch for one picture.
        private readonly float[] cellSum;
        private readonly int[] cellCount;
        private readonly List<int> chosenCells = new List<int>();
        private Vector3[] level = new Vector3[0];
        private bool[] walkable = new bool[0];
        private int[] component = new int[0];
        private int[] queue = new int[0];
        /// <summary>What the Kinect's tilt sensor said when <see cref="Frame"/> was set up, to tell when it's moved.</summary>
        private Vector3? frameHint;
        private int frameCount;
        private int framesSinceBuild;

        /// <summary>The map covers this many meters each way, starting at the Kinect.</summary>
        private const float GridSize = 4f;
        /// <summary>Picks the arena afresh after this many pictures it looked at, about one and a half times a second.</summary>
        private const int FramesPerBuild = 10;
        /// <summary>How much each new picture counts in a cell's height and score.</summary>
        private const float Blend = 0.3f;
        /// <summary>A cell not seen for this many pictures is forgotten, e.g. after the Kinect turns away.</summary>
        private const int ForgetAfter = 30;
        /// <summary>If the Kinect's idea of up moves more than this many degrees, it was moved: start over.</summary>
        private const float MaxUpChange = 2f;
        /// <summary>
        /// Surfaces facing up within this many degrees count as ground to play on. It's generous,
        /// since the Kinect can read a flat table seen at a shallow angle as a steep ramp; walls
        /// and the sides of things are steeper.
        /// </summary>
        private const float MaxSlope = 65f;
        private const float MaxDistance = 4f;

        public SurfaceTracker()
        {
            reference = new float[grid.CellCount];
            score = new float[grid.CellCount];
            lastSeen = new int[grid.CellCount];
            cellSum = new float[grid.CellCount];
            cellCount = new int[grid.CellCount];
            Reset();
        }

        /// <summary>Forgets everything seen, e.g. after switching to a different Kinect.</summary>
        public void Reset()
        {
            Frame = null;
            Map = null;
            Obstacles = null;
            IsFrozen = false;
            for (var i = 0; i < reference.Length; i++)
            {
                reference[i] = float.NaN;
                score[i] = 0f;
                lastSeen[i] = int.MinValue / 2;
            }
            framesSinceBuild = 0;
            Version++;
        }

        /// <summary>Holds the arena still for a round and starts watching for objects on it. Needs a <see cref="Map"/>.</summary>
        public void Freeze()
        {
            if (Map == null) throw new InvalidOperationException("No arena to play on yet.");
            IsFrozen = true;
            Obstacles = new ObstacleMap(grid);
        }

        /// <summary>Goes back to following the surface, after a round.</summary>
        public void Unfreeze()
        {
            IsFrozen = false;
            Obstacles = null;
        }

        /// <summary>
        /// Takes in a new depth picture, using every <paramref name="step"/>th pixel each way.
        /// Returns whether <see cref="Map"/> changed.
        /// </summary>
        public bool Update(DepthFrame depth, int step = 1)
        {
            frameCount++;
            if (!IsFrozen && FrameMoved(depth))
            {
                // Up is the way the picture's level surfaces face, so heights agree with the depth
                // itself; the Kinect's tilt sensor only narrows the search, as on a real desk it
                // was 15 degrees out from what the depth showed.
                var up = UpFinder.Find(depth);
                if (up == null) return false;
                Reset();
                Frame = LevelFrame(up.Value);
                frameHint = HasHint(depth) ? Vector3.Normalize(depth.UpHint.Value) : (Vector3?)null;
            }
            if (Frame == null) return false;

            if (IsFrozen)
            {
                WatchForObjects(depth, step);
                return false;
            }

            // Between rounds, every other picture is plenty to follow the surface, and keeps it quick.
            if (frameCount % 2 == 1) return false;
            Sample(depth, Math.Max(step, depth.Width / 256), out var columns, out var rows);
            FindPlaySurface(columns, rows);
            if (++framesSinceBuild < FramesPerBuild) return false;
            framesSinceBuild = 0;
            var heights = new float[reference.Length];
            var surface = new bool[reference.Length];
            for (var i = 0; i < heights.Length; i++)
            {
                var fresh = frameCount - lastSeen[i] <= ForgetAfter;
                heights[i] = fresh ? reference[i] : float.NaN;
                surface[i] = fresh && score[i] >= 0.5f && !float.IsNaN(reference[i]);
            }
            Map = SurfaceMap.Build(Frame, grid, heights, surface);
            Version++;
            return true;
        }

        /// <summary>Where the ball can go: the arena, minus the objects on it during a round.</summary>
        public IPlayArea PlayArea()
        {
            var map = Map;
            var obstacles = Obstacles;
            return new PlayArea((x, z) => map.Contains(x, z) && (obstacles == null || !obstacles.IsBlocked(x, z)));
        }

        /// <summary>Converts a camera-space point, like a tracked hand, to level coordinates.</summary>
        public Vector3 ToLevel(Vector3 camera) => Frame.ToTable(camera);

        /// <summary>
        /// Whether level coordinates need setting up again: at the start, or because the Kinect's
        /// tilt sensor says it was tilted since they were.
        /// </summary>
        private bool FrameMoved(DepthFrame depth)
        {
            if (Frame == null) return true;
            if (!HasHint(depth) || frameHint == null) return false;
            var cos = Vector3.Dot(Vector3.Normalize(depth.UpHint.Value), frameHint.Value);
            return cos < Math.Cos(MaxUpChange * Math.PI / 180.0);
        }

        private static bool HasHint(DepthFrame depth) =>
            depth.UpHint is Vector3 hint && Math.Abs(hint.Length() - 1f) < 0.1f;

        /// <summary>
        /// Level coordinates with the Kinect at height 0 and the map's middle 2 m in front of it,
        /// so the map covers from the Kinect out to 4 m.
        /// </summary>
        private static TableFrame LevelFrame(Vector3 up)
        {
            var at = new TableFrame(Vector3.Zero, up);
            return new TableFrame(at.Forward * (GridSize / 2), up);
        }

        /// <summary>
        /// Every <paramref name="step"/>th pixel in level coordinates, into <see cref="level"/>,
        /// and whether the surface there faces up enough to play on, into <see cref="walkable"/>:
        /// its direction comes from the pixels around it, which must all be on the same surface,
        /// with no jump in depth between them.
        /// </summary>
        private void Sample(DepthFrame depth, int step, out int columns, out int rows)
        {
            columns = depth.Width / step;
            rows = depth.Height / step;
            var count = columns * rows;
            if (level.Length != count)
            {
                level = new Vector3[count];
                walkable = new bool[count];
                component = new int[count];
                queue = new int[count];
            }
            var minUp = (float)Math.Cos(MaxSlope * Math.PI / 180.0);
            var up = Frame.Up;
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var i = row * columns + column;
                    int x = column * step, y = row * step;
                    var p = depth[x, y];
                    walkable[i] = false;
                    if (!DepthFrame.IsValid(p) || p.Z > MaxDistance)
                    {
                        level[i] = DepthFrame.Invalid;
                        continue;
                    }
                    level[i] = Frame.ToTable(p);
                    var reach = 2 * step;
                    if (x < reach || y < reach || x + reach >= depth.Width || y + reach >= depth.Height) continue;
                    var left = depth[x - reach, y];
                    var right = depth[x + reach, y];
                    var above = depth[x, y - reach];
                    var below = depth[x, y + reach];
                    var limit = Continuity(p.Z) * 2f;
                    if (!Near(left, p, limit) || !Near(right, p, limit) || !Near(above, p, limit) || !Near(below, p, limit)) continue;
                    var normal = Vector3.Cross(right - left, below - above);
                    if (normal.LengthSquared() < 1e-12f) continue;
                    normal = Vector3.Normalize(normal);
                    if (Vector3.Dot(normal, p) > 0f) normal = -normal;
                    walkable[i] = Vector3.Dot(normal, up) >= minUp;
                }
            }
        }

        /// <summary>How far apart in depth two neighboring samples can be and still be one surface.</summary>
        private static float Continuity(float z) => 0.02f + 0.02f * z * z;

        private static bool Near(Vector3 neighbor, Vector3 p, float limit) =>
            DepthFrame.IsValid(neighbor) && Math.Abs(neighbor.Z - p.Z) <= limit;

        /// <summary>
        /// Finds the surface to play on in this picture and adds it to the running map: the
        /// pieces of up-facing surface joined across the picture without jumps in depth, laid on
        /// the grid; of those big enough to play on, the highest, so it's the table and not the
        /// floor past it. Objects are separate pieces, since their sides are steep.
        /// </summary>
        private void FindPlaySurface(int columns, int rows)
        {
            Array.Clear(component, 0, component.Length);
            var pieces = new List<float>();
            for (var start = 0; start < walkable.Length; start++)
            {
                if (!walkable[start] || component[start] != 0) continue;
                var id = pieces.Count + 1;
                int head = 0, tail = 0;
                queue[tail++] = start;
                component[start] = id;
                double heightSum = 0;
                while (head < tail)
                {
                    var i = queue[head++];
                    heightSum += level[i].Y;
                    int row = i / columns, column = i % columns;
                    var limit = Continuity((level[i] - Frame.CameraPosition).Length()) * 1.5f;
                    void Visit(int r, int c)
                    {
                        if (r < 0 || r >= rows || c < 0 || c >= columns) return;
                        var j = r * columns + c;
                        if (!walkable[j] || component[j] != 0) return;
                        if (Vector3.Distance(level[j], level[i]) > limit) return;
                        component[j] = id;
                        queue[tail++] = j;
                    }
                    Visit(row - 1, column);
                    Visit(row + 1, column);
                    Visit(row, column - 1);
                    Visit(row, column + 1);
                }
                pieces.Add((float)(heightSum / tail));
            }

            // How many grid cells each piece covers, so pieces are compared by real size.
            var cellsOf = new int[pieces.Count + 1];
            var seenBy = new int[grid.CellCount];
            for (var i = 0; i < walkable.Length; i++)
            {
                var id = component[i];
                if (id == 0) continue;
                var cell = grid.CellAt(level[i].X, level[i].Z);
                if (cell < 0 || seenBy[cell] == id) continue;
                seenBy[cell] = id;
                cellsOf[id]++;
            }
            var biggest = 0;
            for (var id = 1; id <= pieces.Count; id++) biggest = Math.Max(biggest, cellsOf[id]);
            var minCells = Math.Max(SurfaceMap.MinArea / (grid.CellSize * grid.CellSize), biggest / 4f);
            var chosen = 0;
            var chosenHeight = float.MinValue;
            for (var id = 1; id <= pieces.Count; id++)
            {
                if (cellsOf[id] < minCells || pieces[id - 1] <= chosenHeight) continue;
                chosen = id;
                chosenHeight = pieces[id - 1];
            }

            // Into the running map: the chosen piece's cells gain score, other cells seen lose some.
            chosenCells.Clear();
            for (var i = 0; i < level.Length; i++)
            {
                if (float.IsNaN(level[i].X)) continue;
                var cell = grid.CellAt(level[i].X, level[i].Z);
                if (cell < 0) continue;
                if (lastSeen[cell] != frameCount)
                {
                    if (frameCount - lastSeen[cell] > ForgetAfter)
                    {
                        reference[cell] = float.NaN;
                        score[cell] = 0f;
                    }
                    lastSeen[cell] = frameCount;
                    cellSum[cell] = 0f;
                    cellCount[cell] = 0;
                    // Until the chosen piece turns up in it, below.
                    score[cell] *= 1f - Blend;
                }
                if (chosen == 0 || component[i] != chosen) continue;
                if (cellCount[cell] == 0)
                {
                    chosenCells.Add(cell);
                    score[cell] /= 1f - Blend;
                }
                cellSum[cell] += level[i].Y;
                cellCount[cell]++;
            }
            foreach (var cell in chosenCells)
            {
                var h = cellSum[cell] / cellCount[cell];
                reference[cell] = float.IsNaN(reference[cell]) ? h : reference[cell] + (h - reference[cell]) * Blend;
                score[cell] += (1f - score[cell]) * Blend;
            }
        }

        /// <summary>Roughly how far the Kinect's depth wanders at <paramref name="z"/> meters, in meters.</summary>
        private static float Noise(float z) => 0.002f + 0.002f * z * z;

        /// <summary>
        /// Feeds the obstacle map how far each point on the arena is above the surface measured
        /// before the round, using every <paramref name="step"/>th pixel, and allowing for the
        /// Kinect's depth getting rougher with distance.
        /// </summary>
        private void WatchForObjects(DepthFrame depth, int step)
        {
            var map = Map;
            Obstacles.BeginFrame();
            for (var y = 0; y < depth.Height; y += step)
            {
                for (var x = 0; x < depth.Width; x += step)
                {
                    var p = depth[x, y];
                    if (!DepthFrame.IsValid(p) || p.Z > MaxDistance) continue;
                    var t = Frame.ToTable(p);
                    var cell = grid.CellAt(t.X, t.Z);
                    if (cell < 0 || !map.IsArena(cell)) continue;
                    var above = t.Y - map.CellHeight(cell);
                    if (above > 0f) above = Math.Max(0f, above - Noise(p.Z) * 2f);
                    Obstacles.Observe(t.X, t.Z, above);
                }
            }
            Obstacles.EndFrame();
        }
    }
}

using System;
using System.Numerics;
using ShapesKinect.Core;
using Xunit;

public class TableTrackerTests
{
    private static TableTracker Calibrated(SimulatedTable room)
    {
        var tracker = new TableTracker();
        Assert.True(tracker.Calibrate(room.Render(random: new Random(1))));
        return tracker;
    }

    /// <summary>Where a point in the simulated room is on the table, as the tracker sees it.</summary>
    private static Vector3 OnTable(TableTracker tracker, SimulatedTable room, float x, float y, float z) =>
        tracker.ToTable(room.RoomToCamera(new Vector3(x, y, z)));

    private static void Watch(TableTracker tracker, SimulatedTable room, int frames)
    {
        var random = new Random(7);
        for (var i = 0; i < frames; i++) tracker.Update(room.Render(random: random));
    }

    [Fact]
    public void FindsTheTableNotTheFloor()
    {
        var room = new SimulatedTable();
        var tracker = Calibrated(room);
        // The sensor is 55 cm above the tabletop; the floor is 75 cm further down.
        Assert.Equal(0.55f, tracker.Surface.Frame.CameraPosition.Y, 2);
        var middle = OnTable(tracker, room, 0f, 0f, 0.6f);
        Assert.Equal(0f, middle.Y, 2);
        Assert.True(tracker.Surface.Contains(middle.X, middle.Z));
    }

    [Fact]
    public void SurfaceStopsAtTheTablesEdges()
    {
        var room = new SimulatedTable();
        var tracker = Calibrated(room);
        // Just inside the far edge and the sides, and well past them.
        foreach (var (x, z, inside) in new[]
        {
            (0f, 0.9f, true), (0.45f, 0.6f, true), (-0.45f, 0.6f, true),
            (0f, 1.1f, false), (0.75f, 0.6f, false), (-0.75f, 0.6f, false),
        })
        {
            var p = OnTable(tracker, room, x, 0f, z);
            Assert.True(tracker.Surface.Contains(p.X, p.Z) == inside, $"({x}, {z}) should be {(inside ? "on" : "off")} the table");
        }
        // Most of the 1.2 x 0.75 m table is in view.
        Assert.InRange(tracker.Surface.Area, 0.5f, 0.95f);
    }

    [Fact]
    public void TableCoordinatesAreNotMirrored()
    {
        var room = new SimulatedTable();
        var tracker = Calibrated(room);
        var left = OnTable(tracker, room, -0.3f, 0f, 0.6f);
        var right = OnTable(tracker, room, 0.3f, 0f, 0.6f);
        var far = OnTable(tracker, room, 0f, 0f, 0.9f);
        var near = OnTable(tracker, room, 0f, 0f, 0.4f);
        Assert.Equal(0.6f, right.X - left.X, 2);
        Assert.Equal(0.5f, far.Z - near.Z, 2);
    }

    [Fact]
    public void ACupOnTheTableBlocksAndClearsWhenRemoved()
    {
        var room = new SimulatedTable();
        var tracker = Calibrated(room);
        room.Objects.Add(Box.OnTable(0.2f, 0.6f, 0.08f, 0.08f, 0.1f));
        Watch(tracker, room, 4);
        var cup = OnTable(tracker, room, 0.2f, 0f, 0.6f);
        Assert.True(tracker.Obstacles.IsBlocked(cup.X, cup.Z));
        Assert.False(tracker.PlayArea().Contains(cup.X, cup.Z));
        var open = OnTable(tracker, room, -0.2f, 0f, 0.6f);
        Assert.True(tracker.PlayArea().Contains(open.X, open.Z));

        room.Objects.Clear();
        Watch(tracker, room, 4);
        Assert.False(tracker.Obstacles.IsBlocked(cup.X, cup.Z));
    }

    [Fact]
    public void AnEmptyTableHasNoPhantomObjects()
    {
        var room = new SimulatedTable();
        var tracker = Calibrated(room);
        Watch(tracker, room, 10);
        var blocked = 0;
        for (var cell = 0; cell < tracker.Obstacles.Grid.CellCount; cell++)
        {
            if (tracker.Obstacles.IsBlocked(cell)) blocked++;
        }
        Assert.Equal(0, blocked);
    }

    [Fact]
    public void AFlatBookCountsButAHoveringHandDoesNot()
    {
        var room = new SimulatedTable();
        var tracker = Calibrated(room);
        room.Objects.Add(Box.OnTable(-0.25f, 0.5f, 0.15f, 0.2f, 0.03f));
        room.Hand = new Vector3(0.2f, 0.25f, 0.6f);
        Watch(tracker, room, 4);
        var book = OnTable(tracker, room, -0.25f, 0f, 0.5f);
        var underHand = OnTable(tracker, room, 0.2f, 0f, 0.6f);
        Assert.True(tracker.Obstacles.IsBlocked(book.X, book.Z));
        Assert.False(tracker.Obstacles.IsBlocked(underHand.X, underHand.Z));
    }

    [Fact]
    public void TheTableEdgeIsOneClosedLoop()
    {
        var room = new SimulatedTable();
        var tracker = Calibrated(room);
        var ends = new System.Collections.Generic.Dictionary<(long, long), int>();
        var segments = 0;
        tracker.Surface.TraceEdge((x1, z1, x2, z2) =>
        {
            segments++;
            foreach (var (x, z) in new[] { (x1, z1), (x2, z2) })
            {
                var key = ((long)Math.Round(x * 10000), (long)Math.Round(z * 10000));
                ends[key] = ends.TryGetValue(key, out var n) ? n + 1 : 1;
            }
        });
        Assert.True(segments > 20);
        foreach (var count in ends.Values) Assert.Equal(0, count % 2);
    }
}

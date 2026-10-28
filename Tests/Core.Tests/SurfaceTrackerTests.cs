using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ShapesKinect.Core;
using Xunit;

public class SurfaceTrackerTests
{
    /// <summary>Feeds the tracker pictures of <paramref name="room"/> until it has had time to pick the arena.</summary>
    private static SurfaceTracker Watch(SimulatedTable room, SurfaceTracker tracker = null, int frames = 25,
        int width = 256, Action<DepthFrame> change = null, int seed = 1)
    {
        tracker = tracker ?? new SurfaceTracker();
        var random = new Random(seed);
        for (var i = 0; i < frames; i++)
        {
            var frame = room.Render(width, width * 212 / 256, random);
            change?.Invoke(frame);
            tracker.Update(frame, width > 300 ? 2 : 1);
        }
        return tracker;
    }

    /// <summary>Where a point in the simulated room is in the tracker's level coordinates.</summary>
    private static Vector3 Level(SurfaceTracker tracker, SimulatedTable room, float x, float y, float z) =>
        tracker.ToLevel(room.RoomToCamera(new Vector3(x, y, z)));

    private static bool InArena(SurfaceTracker tracker, SimulatedTable room, float x, float z)
    {
        var p = Level(tracker, room, x, 0f, z);
        return tracker.Map.Contains(p.X, p.Z);
    }

    [Theory]
    // Looking almost straight across the table, from just above it.
    [InlineData(10f, 0.2f, 0.6f)]
    [InlineData(15f, 0.3f, 0.3f)]
    // The usual setup, and a steeper one.
    [InlineData(30f, 0.4f, 0.3f)]
    [InlineData(60f, 0.6f, 0.3f)]
    // A table well away from the Kinect, where the floor fills more of the picture than the table.
    [InlineData(20f, 0.4f, 1.0f)]
    [InlineData(30f, 0.6f, 1.0f)]
    public void FindsTheTableWhereverTheKinectIs(float pitch, float height, float near)
    {
        foreach (var withHint in new[] { true, false })
        {
            var room = new SimulatedTable
            {
                PitchDegrees = pitch, SensorPosition = new Vector3(0f, height, -0.1f), TableNear = near,
                TableWidth = 1.0f, TableDepth = 0.6f, Noise = 0.004f,
            };
            var tracker = Watch(room, width: 512, change: f => { if (!withHint) f.UpHint = null; });
            Assert.NotNull(tracker.Map);
            Assert.True(InArena(tracker, room, 0f, near + 0.3f), $"middle of the table, hint {withHint}");
            Assert.False(InArena(tracker, room, 0f, near + 0.75f), "past the far edge");
            // Up is worked out from the picture, and a degree off shifts heights a few
            // centimeters a couple of meters out; everything shifts together, so play is the same.
            Assert.InRange(tracker.Map.MeanHeight, -height - 0.05f, -height + 0.05f);
            Assert.InRange(tracker.Map.Area, 0.05f, 0.7f);
        }
    }

    [Fact]
    public void TheArenaFollowsTheSurfaceAsMeasuredEvenWhereThatsWrong()
    {
        // Like a real capture: from 18 cm above a desk, the Kinect read the back of it as rising
        // several centimeters. Here the depth of the far part is pulled in along each ray.
        var room = new SimulatedTable
        {
            PitchDegrees = 15f, SensorPosition = new Vector3(0f, 0.18f, -0.1f), TableNear = 0.5f,
            TableDepth = 0.4f, WallDistance = 0.95f,
        };
        void Distort(DepthFrame frame)
        {
            for (var i = 0; i < frame.Points.Length; i++)
            {
                var p = frame.Points[i];
                if (!DepthFrame.IsValid(p)) continue;
                var r = room.CameraToRoom(p);
                if (Math.Abs(r.Y) > 0.003f || r.Z < room.TableNear) continue;
                var shrink = 1f - 0.3f * (r.Z - room.TableNear);
                frame.Points[i] = p * shrink;
            }
        }
        var tracker = Watch(room, width: 512, change: Distort);
        Assert.NotNull(tracker.Map);
        Assert.True(InArena(tracker, room, 0f, 0.6f), "front of the desk");
        // The back half reads as raised, but is still part of the arena.
        var backAsMeasured = tracker.ToLevel(room.RoomToCamera(new Vector3(0f, 0f, 0.82f)) * (1f - 0.3f * 0.32f));
        Assert.True(tracker.Map.Contains(backAsMeasured.X, backAsMeasured.Z), "back of the desk, where it's measured");
        // Something drawn on the arena there sits where the desk is measured, so it's on the
        // Kinect's line of sight to the real desk: in the picture, it's on the desk.
        var drawn = tracker.Map.OnSurface(backAsMeasured.X, backAsMeasured.Z);
        Assert.InRange(drawn.Y - backAsMeasured.Y, -0.012f, 0.012f);
    }

    [Fact]
    public void ACupDuringARoundBlocksAndClearsWhenRemoved()
    {
        var room = new SimulatedTable();
        var tracker = Watch(room);
        tracker.Freeze();
        room.Objects.Add(Box.OnTable(0.2f, 0.6f, 0.08f, 0.08f, 0.1f));
        Watch(room, tracker, frames: 4);
        var cup = Level(tracker, room, 0.2f, 0f, 0.6f);
        Assert.True(tracker.Obstacles.IsBlocked(cup.X, cup.Z));
        Assert.False(tracker.PlayArea().Contains(cup.X, cup.Z));
        var open = Level(tracker, room, -0.2f, 0f, 0.6f);
        Assert.True(tracker.PlayArea().Contains(open.X, open.Z));

        room.Objects.Clear();
        Watch(room, tracker, frames: 4);
        Assert.False(tracker.Obstacles.IsBlocked(cup.X, cup.Z));
    }

    [Fact]
    public void AnEmptyTableHasNoPhantomObjects()
    {
        var room = new SimulatedTable { Noise = 0.004f };
        var tracker = Watch(room, width: 512);
        tracker.Freeze();
        Watch(room, tracker, frames: 10, width: 512, seed: 5);
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
        var tracker = Watch(room);
        tracker.Freeze();
        room.Objects.Add(Box.OnTable(-0.25f, 0.5f, 0.15f, 0.2f, 0.03f));
        room.Hand = new Vector3(0.2f, 0.25f, 0.6f);
        Watch(room, tracker, frames: 4);
        var book = Level(tracker, room, -0.25f, 0f, 0.5f);
        var underHand = Level(tracker, room, 0.2f, 0f, 0.6f);
        Assert.True(tracker.Obstacles.IsBlocked(book.X, book.Z));
        Assert.False(tracker.Obstacles.IsBlocked(underHand.X, underHand.Z));
    }

    [Fact]
    public void ThingsAlreadyOnTheTableAreHolesInTheArena()
    {
        var room = new SimulatedTable();
        room.Objects.Add(Box.OnTable(0.25f, 0.65f, 0.1f, 0.1f, 0.1f));
        var tracker = Watch(room);
        Assert.False(InArena(tracker, room, 0.25f, 0.65f));
        Assert.True(InArena(tracker, room, -0.25f, 0.65f));
    }

    [Fact]
    public void TheArenaStaysPutDuringARound()
    {
        var room = new SimulatedTable();
        var tracker = Watch(room);
        tracker.Freeze();
        var map = tracker.Map;
        room.Objects.Add(Box.OnTable(0f, 0.6f, 0.3f, 0.3f, 0.05f));
        Watch(room, tracker, frames: 25);
        Assert.Same(map, tracker.Map);
        tracker.Unfreeze();
        Watch(room, tracker, frames: 25);
        Assert.NotSame(map, tracker.Map);
        Assert.False(InArena(tracker, room, 0f, 0.6f));
    }

    [Fact]
    public void TiltingTheKinectStartsOver()
    {
        var room = new SimulatedTable();
        var tracker = Watch(room);
        var before = tracker.Frame;
        room.PitchDegrees = 35f;
        Watch(room, tracker);
        Assert.NotSame(before, tracker.Frame);
        Assert.NotNull(tracker.Map);
        Assert.True(InArena(tracker, room, 0f, 0.6f));
    }

    [Fact]
    public void AWallIsNotAnArena()
    {
        // Pointed level at the wall, too high to see the table or floor.
        var room = new SimulatedTable { PitchDegrees = 0f, SensorPosition = new Vector3(0f, 1.5f, -0.1f) };
        var tracker = Watch(room, width: 512);
        Assert.Null(tracker.Map);
    }

    [Fact]
    public void LevelCoordinatesAreNotMirrored()
    {
        var room = new SimulatedTable();
        var tracker = Watch(room);
        var left = Level(tracker, room, -0.3f, 0f, 0.6f);
        var right = Level(tracker, room, 0.3f, 0f, 0.6f);
        var far = Level(tracker, room, 0f, 0f, 0.9f);
        var near = Level(tracker, room, 0f, 0f, 0.4f);
        Assert.Equal(0.6f, right.X - left.X, 2);
        Assert.Equal(0.5f, far.Z - near.Z, 2);
        Assert.Equal(0f, right.Y - far.Y, 3);
    }

    [Fact]
    public void TheArenaEdgeIsClosedLoops()
    {
        var tracker = Watch(new SimulatedTable());
        var ends = new Dictionary<(long, long), int>();
        var segments = 0;
        tracker.Map.TraceEdge((x1, z1, x2, z2) =>
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

    [Fact]
    public void ARoundStartsInTheArena()
    {
        var tracker = Watch(new SimulatedTable());
        Assert.True(tracker.Map.Contains(tracker.Map.StartX, tracker.Map.StartZ));
    }

    [Fact]
    public void CapturesSaveAndLoad()
    {
        var frame = new SimulatedTable().Render(64, 53, new Random(1));
        frame.UpHint = new Vector3(0f, 0.7f, -0.7f);
        var stream = new MemoryStream();
        DepthCapture.Save(frame, stream);
        stream.Position = 0;
        var loaded = DepthCapture.Load(stream);
        Assert.Equal(frame.Width, loaded.Width);
        Assert.Equal(frame.Height, loaded.Height);
        Assert.Equal(frame.UpHint, loaded.UpHint);
        for (var i = 0; i < frame.Points.Length; i++)
        {
            Assert.True(frame.Points[i] == loaded.Points[i] || (!DepthFrame.IsValid(frame.Points[i]) && !DepthFrame.IsValid(loaded.Points[i])));
        }
    }
}

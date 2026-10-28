using System;
using System.Numerics;
using ShapesKinect.Core;
using Xunit;

public class CardTests
{
    private static GrayImage Picture(SimulatedTable room, PinholeCamera camera, int seed = 1)
    {
        var image = new GrayImage(960, 540, 2);
        room.RenderColor(camera, image, new Random(seed));
        return image;
    }

    /// <summary>Where a point on the simulated table shows up in the color picture.</summary>
    private static Vector2 Seen(SimulatedTable room, PinholeCamera camera, float x, float z) =>
        camera.Project(room.RoomToCamera(new Vector3(x, 0f, z)));

    /// <summary>The printed card's corners on the simulated table: top left, top right, bottom right, bottom left.</summary>
    private static Vector3[] TrueCorners(SimulatedTable room)
    {
        var (_, cx, cz, turn) = room.Card.Value;
        var right = new Vector3((float)Math.Cos(turn), 0f, -(float)Math.Sin(turn));
        var top = new Vector3((float)Math.Sin(turn), 0f, (float)Math.Cos(turn));
        var middle = new Vector3(cx, 0f, cz);
        var h = room.CardSize / 2;
        return new[] { middle - right * h + top * h, middle + right * h + top * h, middle + right * h - top * h, middle - right * h - top * h };
    }

    [Theory]
    // The usual setup: at the table's edge, looking down across it.
    [InlineData(45f, 0.55f, 0, 0.6f, 0f, false)]
    [InlineData(45f, 0.55f, 1, 0.5f, 0.5f, true)]
    // Turned the other way round, further off, to the side.
    [InlineData(35f, 0.45f, 2, 0.8f, 3.0f, true)]
    // From low down, across the table.
    [InlineData(20f, 0.3f, 0, 0.55f, -0.7f, true)]
    // Looking steeply down.
    [InlineData(70f, 0.8f, 1, 0.4f, 1.2f, false)]
    public void FindsAndPlacesTheCard(float pitch, float height, int id, float z, float turn, bool mirrored)
    {
        var room = new SimulatedTable { PitchDegrees = pitch, SensorPosition = new Vector3(0f, height, -0.1f), Card = (id, 0.1f, z, turn), Noise = 0.003f };
        var camera = SimulatedTable.ColorCamera(mirrored);
        var image = Picture(room, camera);
        var sightings = new CardDetector().Detect(image);
        Assert.Single(sightings);
        var seen = sightings[0];
        Assert.Equal(id, seen.Id);
        // Its corners, in the printed order, within a pixel or so.
        var truth = TrueCorners(room);
        for (var i = 0; i < 4; i++)
        {
            var expected = camera.Project(room.RoomToCamera(truth[i]));
            Assert.True(Vector2.Distance(expected, seen.Corners[i]) < 2.5f, $"corner {i}: {seen.Corners[i]} vs {expected}");
        }

        foreach (var depth in new[] { room.Render(512, 424, new Random(2)), null })
        {
            var placement = CardLocator.Locate(seen, camera, depth);
            Assert.NotNull(placement);
            Assert.Equal(depth != null, placement.FromDepth);
            var frame = placement.Frame;
            var middle = room.RoomToCamera(new Vector3(0.1f, 0f, z));
            Assert.True(Vector3.Distance(frame.Origin, middle) < (depth != null ? 0.01f : 0.03f), $"middle {frame.Origin} vs {middle}, depth {depth != null}");
            // Up out of the table, and Z toward the card's printed top.
            var up = room.RoomToCamera(new Vector3(0f, 1f, 0f)) - room.RoomToCamera(Vector3.Zero);
            Assert.True(Vector3.Dot(frame.Up, up) > (depth != null ? 0.995f : 0.97f), $"up {frame.Up} vs {up}");
            var top = room.RoomToCamera(truth[0] + truth[1]) - room.RoomToCamera(truth[2] + truth[3]);
            Assert.True(Vector3.Dot(frame.Forward, Vector3.Normalize(top)) > 0.99f, $"forward {frame.Forward} vs {top}");
            // And to the card's right as printed, so the arena isn't mirrored.
            var right = room.RoomToCamera(truth[1]) - room.RoomToCamera(truth[0]);
            Assert.True(Vector3.Dot(frame.Right, Vector3.Normalize(right)) > 0.99f, $"right {frame.Right} vs {right}");
            Assert.Equal(room.CardSize, placement.Size, 2);
        }
    }

    [Fact]
    public void NoCardNoSightings()
    {
        var room = new SimulatedTable();
        room.Objects.Add(Box.OnTable(0.2f, 0.6f, 0.1f, 0.1f, 0.1f));
        room.Objects.Add(Box.OnTable(-0.25f, 0.5f, 0.15f, 0.2f, 0.03f));
        Assert.Empty(new CardDetector().Detect(Picture(room, SimulatedTable.ColorCamera(true))));
    }

    [Fact]
    public void AHandOverTheCardHidesIt()
    {
        var room = new SimulatedTable { Card = (0, 0f, 0.6f, 0f), Hand = new Vector3(0f, 0.08f, 0.6f), HandRadius = 0.07f };
        Assert.Empty(new CardDetector().Detect(Picture(room, SimulatedTable.ColorCamera(true))));
    }

    [Fact]
    public void TheTrackerHoldsTheCardBrieflyWhenHiddenThenLetsGo()
    {
        var room = new SimulatedTable { Card = (2, 0f, 0.6f, 0.3f) };
        var camera = SimulatedTable.ColorCamera(true);
        var depth = room.Render(512, 424, new Random(1));
        var tracker = new CardTracker();
        tracker.Update(Picture(room, camera), camera, depth, 0f);
        Assert.True(tracker.IsVisible);
        Assert.Equal(2, tracker.Current.Id);
        // Seen again: the quick search near where it was finds it.
        tracker.Update(Picture(room, camera, 2), camera, depth, 0.1f);
        Assert.True(tracker.IsVisible);

        room.Card = null;
        tracker.Update(Picture(room, camera, 3), camera, depth, 0.5f);
        Assert.False(tracker.IsVisible);
        Assert.NotNull(tracker.Current);
        tracker.Update(Picture(room, camera, 4), camera, depth, 0.5f + CardTracker.HoldSeconds + 0.1f);
        Assert.Null(tracker.Current);
    }

    [Fact]
    public void TheTrackerFollowsAMovedCard()
    {
        var room = new SimulatedTable { Card = (0, 0f, 0.6f, 0f) };
        var camera = SimulatedTable.ColorCamera(true);
        var depth = room.Render(512, 424, new Random(1));
        var tracker = new CardTracker();
        tracker.Update(Picture(room, camera), camera, depth, 0f);
        room.Card = (0, -0.25f, 0.75f, 1f);
        tracker.Update(Picture(room, camera, 2), camera, depth, 0.1f);
        Assert.True(tracker.IsVisible);
        var middle = room.RoomToCamera(new Vector3(-0.25f, 0f, 0.75f));
        Assert.True(Vector3.Distance(tracker.Current.Frame.Origin, middle) < 0.01f);
    }
    private static double Gauss(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());

    [Theory]
    [InlineData(45f, 0.55f, 0.6f)]
    // Near the table's far edge, where the ring of table around the card runs into the wall.
    [InlineData(35f, 0.5f, 0.9f)]
    public void ACardLyingStillHoldsStill(float pitch, float height, float z)
    {
        var room = new SimulatedTable { PitchDegrees = pitch, SensorPosition = new Vector3(0f, height, -0.1f), Card = (0, 0.05f, z, 0.3f), Noise = 0.003f };
        var camera = SimulatedTable.ColorCamera(true);
        var tracker = new CardTracker();
        var random = new Random(5);
        var (_, cx, cz, turn) = room.Card.Value;
        var origins = new System.Collections.Generic.List<Vector3>();
        var ups = new System.Collections.Generic.List<Vector3>();
        for (var i = 0; i < 30; i++)
        {
            // A real camera: brightness drifting as exposure adjusts, and grain.
            var image = Picture(room, camera, i);
            var gain = 0.6 + 0.1 * Math.Sin(i * 0.7);
            for (var k = 0; k < image.Pixels.Length; k++) image.Pixels[k] = (byte)Math.Max(0, Math.Min(255, image.Pixels[k] * gain + Gauss(random) * 6));
            // Black ink barely reflects the depth camera's infrared: few readings there, and wild ones.
            var depth = room.Render(512, 424, random);
            for (var k = 0; k < depth.Points.Length; k++)
            {
                var p = depth.Points[k];
                if (!DepthFrame.IsValid(p)) continue;
                var r = room.CameraToRoom(p);
                float dx = r.X - cx, dz = r.Z - cz, c = (float)Math.Cos(turn), s = (float)Math.Sin(turn);
                if (Math.Abs(r.Y) > 0.01f || Math.Abs(dx * c - dz * s) > room.CardSize / 2 || Math.Abs(dx * s + dz * c) > room.CardSize / 2) continue;
                depth.Points[k] = random.NextDouble() < 0.6 ? DepthFrame.Invalid : p * (1f + (float)Gauss(random) * 0.02f);
            }
            tracker.Update(image, camera, depth, i / 30f);
            if (i < 10 || tracker.Current == null) continue;
            origins.Add(tracker.Current.Frame.Origin);
            ups.Add(tracker.Current.Frame.Up);
        }
        Assert.True(origins.Count >= 15);
        var trueUp = room.RoomToCamera(Vector3.UnitY) - room.RoomToCamera(Vector3.Zero);
        var middle = room.RoomToCamera(new Vector3(cx, 0f, cz));
        foreach (var o in origins) Assert.True(Vector3.Distance(o, middle) < 0.004f, $"middle {o} vs {middle}");
        // Within a third of a degree, so the arena's far corners stay within a couple of millimeters.
        foreach (var u in ups) Assert.True(Vector3.Dot(u, trueUp) > Math.Cos(Math.PI / 540), $"up {u} vs {trueUp}");
    }
    [Theory]
    [InlineData(0.15f)]
    // Printed "to fit the page", a little small.
    [InlineData(0.141f)]
    public void ACardDoesntJumpWhenDepthComesAndGoes(float printed)
    {
        var room = new SimulatedTable { Card = (0, 0.05f, 0.7f, 0.3f), CardSize = printed };
        var camera = SimulatedTable.ColorCamera(true);
        var tracker = new CardTracker();
        var random = new Random(7);
        Vector3? before = null;
        for (var i = 0; i < 30; i++)
        {
            var depth = room.Render(512, 424, random);
            // Every third picture, no depth at all.
            if (i % 3 == 2) for (var k = 0; k < depth.Points.Length; k++) depth.Points[k] = DepthFrame.Invalid;
            tracker.Update(Picture(room, camera, i), camera, depth, i / 30f);
            var origin = tracker.Current.Frame.Origin;
            if (i >= 3 && before != null) Assert.True(Vector3.Distance(origin, before.Value) < 0.001f, $"picture {i}: moved {Vector3.Distance(origin, before.Value) * 1000:0.0} mm");
            before = origin;
        }
    }
    [Fact]
    public void RoughTableDepthDoesntTiltTheArena()
    {
        // Like a real desk seen at a shallow angle: every depth reading about a centimeter out.
        var room = new SimulatedTable { PitchDegrees = 22f, SensorPosition = new Vector3(0f, 0.35f, -0.1f), Card = (0, 0.05f, 0.75f, 0.2f) };
        var camera = SimulatedTable.ColorCamera(true);
        var tracker = new CardTracker();
        var random = new Random(3);
        var trueUp = room.RoomToCamera(Vector3.UnitY) - room.RoomToCamera(Vector3.Zero);
        for (var i = 0; i < 20; i++)
        {
            var depth = room.Render(512, 424, random);
            for (var k = 0; k < depth.Points.Length; k++)
            {
                if (DepthFrame.IsValid(depth.Points[k])) depth.Points[k] += new Vector3(0f, 0f, (float)Gauss(random) * 0.012f);
            }
            tracker.Update(Picture(room, camera, i), camera, depth, i / 30f);
            var up = tracker.Current.Frame.Up;
            Assert.True(Vector3.Dot(up, trueUp) > Math.Cos(Math.PI / 360), $"picture {i}: up {up} vs {trueUp}");
        }
        var middle = room.RoomToCamera(new Vector3(0.05f, 0f, 0.75f));
        Assert.True(Vector3.Distance(tracker.Current.Frame.Origin, middle) < 0.005f);
    }
}

public class BoardTests
{
    private static (SimulatedTable room, Board board, BoardTracker tracker) Play(int card = 0)
    {
        var room = new SimulatedTable { Card = (card, 0f, 0.6f, 0f) };
        var camera = SimulatedTable.ColorCamera(true);
        var depth = room.Render(512, 424, new Random(1));
        var image = new GrayImage(960, 540, 2);
        room.RenderColor(camera, image, new Random(1));
        var placement = CardLocator.Locate(new CardDetector().Detect(image)[0], camera, depth);
        var board = new Board(ArenaLayout.ForCard(card), placement.Frame);
        var tracker = new BoardTracker(board);
        Watch(tracker, room, 16);
        return (room, board, tracker);
    }

    private static void Watch(BoardTracker tracker, SimulatedTable room, int frames)
    {
        var random = new Random(9);
        for (var i = 0; i < frames; i++) tracker.Update(room.Render(512, 424, random), 2);
    }

    private static Vector3 OnBoard(Board board, SimulatedTable room, float x, float z) =>
        board.Frame.ToTable(room.RoomToCamera(new Vector3(x, 0f, z)));

    [Fact]
    public void TheBoardIsBuiltAroundTheCard()
    {
        var (room, board, _) = Play();
        var middle = OnBoard(board, room, 0f, 0.6f);
        Assert.InRange(middle.Length(), 0f, 0.01f);
        // 60 cm across, 40 cm along, square to the card.
        Assert.True(board.Contains(OnBoard(board, room, 0.28f, 0.6f).X, OnBoard(board, room, 0.28f, 0.6f).Z));
        var past = OnBoard(board, room, 0.32f, 0.6f);
        Assert.False(board.Contains(past.X, past.Z));
        var far = OnBoard(board, room, 0f, 0.78f);
        Assert.True(board.Contains(far.X, far.Z));
        Assert.Equal(0f, far.Y, 2);
    }

    [Fact]
    public void ArenaWallsBlockTheBall()
    {
        var (_, board, tracker) = Play(1);
        var pillar = board.Layout.Walls[0];
        Assert.False(tracker.PlayArea().Contains((pillar.X1 + pillar.X2) / 2, (pillar.Z1 + pillar.Z2) / 2));
        Assert.True(tracker.PlayArea().Contains(0f, 0f));
    }

    [Fact]
    public void EveryArenaStartsTheBallInTheOpen()
    {
        foreach (var layout in ArenaLayout.All)
        {
            var board = new Board(layout, new TableFrame(Vector3.Zero, Vector3.UnitY));
            var r = new RoundRules().PlayerRadius;
            foreach (var (x, z) in new[] { (0f, 0f), (r, 0f), (-r, 0f), (0f, r), (0f, -r) }) Assert.True(board.IsOpen(x, z), layout.Name);
        }
    }

    [Fact]
    public void ACupOnTheBoardBlocksAndClearsWhenRemoved()
    {
        var (room, board, tracker) = Play();
        Assert.True(tracker.IsReady);
        room.Objects.Add(Box.OnTable(0.15f, 0.6f, 0.08f, 0.08f, 0.1f));
        Watch(tracker, room, 5);
        var cup = OnBoard(board, room, 0.15f, 0.6f);
        Assert.True(tracker.Obstacles.IsBlocked(cup.X, cup.Z));
        Assert.False(tracker.PlayArea().Contains(cup.X, cup.Z));
        room.Objects.Clear();
        Watch(tracker, room, 5);
        Assert.False(tracker.Obstacles.IsBlocked(cup.X, cup.Z));
    }

    [Fact]
    public void AHoveringHandIsNotAnObject()
    {
        var (room, board, tracker) = Play();
        room.Hand = new Vector3(0.15f, 0.25f, 0.65f);
        Watch(tracker, room, 5);
        var underHand = OnBoard(board, room, 0.15f, 0.65f);
        Assert.False(tracker.Obstacles.IsBlocked(underHand.X, underHand.Z));
    }

    [Fact]
    public void MovingTheCardLooksAfresh()
    {
        var (room, board, tracker) = Play();
        room.Objects.Add(Box.OnTable(0.15f, 0.6f, 0.08f, 0.08f, 0.1f));
        Watch(tracker, room, 5);
        var frame = board.Frame;
        board.MoveTo(new TableFrame(frame.Origin + frame.Right * 0.05f, frame.Up, frame.Forward));
        Watch(tracker, room, 2);
        Assert.False(tracker.IsReady);
        var nothing = 0;
        for (var cell = 0; cell < tracker.Obstacles.Grid.CellCount; cell++) if (tracker.Obstacles.IsBlocked(cell)) nothing++;
        Assert.Equal(0, nothing);
        Watch(tracker, room, 16);
        Assert.True(tracker.IsReady);
    }

    [Fact]
    public void ASurfaceMeasuredSlightlyWrongHasNoPhantomObjects()
    {
        var (room, board, _) = Play();
        // From low down the Kinect reads the back of a table as rising; here the depth of the
        // far part is pulled in along each ray.
        void Distort(DepthFrame frame)
        {
            for (var i = 0; i < frame.Points.Length; i++)
            {
                var p = frame.Points[i];
                if (!DepthFrame.IsValid(p)) continue;
                var r = room.CameraToRoom(p);
                if (Math.Abs(r.Y) > 0.003f || r.Z < 0.6f) continue;
                frame.Points[i] = p * (1f - 0.15f * (r.Z - 0.6f));
            }
        }
        var tracker = new BoardTracker(board);
        var random = new Random(4);
        for (var i = 0; i < 16; i++)
        {
            var frame = room.Render(512, 424, random);
            Distort(frame);
            tracker.Update(frame, 2);
        }
        var blocked = 0;
        for (var cell = 0; cell < tracker.Obstacles.Grid.CellCount; cell++) if (tracker.Obstacles.IsBlocked(cell)) blocked++;
        Assert.Equal(0, blocked);
    }

    [Fact]
    public void CapturesSaveAndLoad()
    {
        var frame = new SimulatedTable().Render(64, 53, new Random(1));
        frame.UpHint = new Vector3(0f, 0.7f, -0.7f);
        var stream = new System.IO.MemoryStream();
        DepthCapture.Save(frame, stream);
        stream.Position = 0;
        var loaded = DepthCapture.Load(stream);
        Assert.Equal(frame.Width, loaded.Width);
        Assert.Equal(frame.UpHint, loaded.UpHint);
        for (var i = 0; i < frame.Points.Length; i++)
        {
            Assert.True(frame.Points[i] == loaded.Points[i] || (!DepthFrame.IsValid(frame.Points[i]) && !DepthFrame.IsValid(loaded.Points[i])));
        }
    }
}

public class ObstacleMapTests
{
    private static ObstacleMap Raise(params (float x, float z)[] spots)
    {
        var map = new ObstacleMap(new Grid(1f, 0.02f));
        for (var frame = 0; frame < 5; frame++)
        {
            map.BeginFrame();
            foreach (var (x, z) in spots)
            {
                map.Observe(x, z, 0.05f);
                map.Observe(x, z, 0.05f);
            }
            map.EndFrame();
        }
        return map;
    }

    [Fact]
    public void AStrayBumpIsNotAnObject()
    {
        var map = Raise((0.11f, 0.11f), (0.13f, 0.11f));
        Assert.False(map.IsBlocked(0.11f, 0.11f));
    }

    [Fact]
    public void ASmallThingIsAnObject()
    {
        // About 4 by 6 cm, like a small box.
        var spots = new System.Collections.Generic.List<(float, float)>();
        for (var x = 0; x < 2; x++) for (var z = 0; z < 3; z++) spots.Add((0.11f + x * 0.02f, 0.11f + z * 0.02f));
        var map = Raise(spots.ToArray());
        Assert.True(map.IsBlocked(0.11f, 0.11f));
    }

    [Fact]
    public void ABumpForTwoFramesIsNotAnObject()
    {
        var map = new ObstacleMap(new Grid(1f, 0.02f));
        for (var frame = 0; frame < 2; frame++)
        {
            map.BeginFrame();
            for (var x = 0; x < 4; x++) for (var z = 0; z < 4; z++) { map.Observe(0.11f + x * 0.02f, 0.11f + z * 0.02f, 0.05f); map.Observe(0.11f + x * 0.02f, 0.11f + z * 0.02f, 0.05f); }
            map.EndFrame();
        }
        Assert.False(map.IsBlocked(0.13f, 0.13f));
    }
}

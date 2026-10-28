using System;
using System.IO;
using System.Numerics;
using ShapesKinect.Core;
using Xunit;

public class BoardTests
{
    private const int Width = 512, Height = 424;

    /// <summary>The depth pixel a point in the simulated room shows up at.</summary>
    private static (int x, int y) PixelOf(SimulatedTable room, Vector3 roomPoint)
    {
        var c = room.RoomToCamera(roomPoint);
        var focal = 365f * Width / 512f;
        return ((int)Math.Round(Width / 2f - focal * c.X / c.Z), (int)Math.Round(Height / 2f - focal * c.Y / c.Z));
    }

    private static PickedSurface PickAt(SimulatedTable room, float x, float y, float z, DepthFrame frame = null)
    {
        frame = frame ?? room.Render(Width, Height, new Random(1));
        var (px, py) = PixelOf(room, new Vector3(x, y, z));
        return SurfacePicker.Pick(frame, px, py);
    }

    /// <summary>Where a point in the simulated room is in <paramref name="frame"/>'s coordinates.</summary>
    private static Vector3 In(TableFrame frame, SimulatedTable room, float x, float y, float z) =>
        frame.ToTable(room.RoomToCamera(new Vector3(x, y, z)));

    [Theory]
    [InlineData(45f, 0.55f, 0.25f)]
    // Looking almost straight across the table, from just above it.
    [InlineData(12f, 0.2f, 0.5f)]
    // Looking steeply down.
    [InlineData(70f, 0.8f, 0.2f)]
    public void PicksTheTableWhereverTheKinectIs(float pitch, float height, float near)
    {
        var room = new SimulatedTable { PitchDegrees = pitch, SensorPosition = new Vector3(0f, height, -0.1f), TableNear = near, Noise = 0.004f };
        var middle = near + room.TableDepth / 2;
        var surface = PickAt(room, 0.1f, 0f, middle);
        Assert.NotNull(surface);
        // Level, at the table's height.
        Assert.Equal(height, surface.Frame.CameraPosition.Y, 1);
        var up = In(surface.Frame, room, 0f, 1f, middle) - In(surface.Frame, room, 0f, 0f, middle);
        Assert.True(up.Y > 0.99f, $"up {up}");
        var p = In(surface.Frame, room, -0.3f, 0f, middle);
        Assert.True(surface.Contains(p.X, p.Z), "elsewhere on the table");
        var past = In(surface.Frame, room, 0.75f, 0f, middle);
        Assert.False(surface.Contains(past.X, past.Z), "past the table's side");
    }

    [Fact]
    public void PickingTheFloorGivesTheFloor()
    {
        var room = new SimulatedTable { PitchDegrees = 30f, SensorPosition = new Vector3(0f, 0.6f, -0.1f), TableNear = 1.0f, TableDepth = 0.5f };
        var surface = PickAt(room, 0f, -room.TableHeight, 0.4f);
        Assert.NotNull(surface);
        Assert.InRange(surface.Frame.CameraPosition.Y, 0.6f + room.TableHeight - 0.03f, 0.6f + room.TableHeight + 0.03f);
        var onTable = In(surface.Frame, room, 0f, 0f, 1.2f);
        Assert.False(surface.Contains(onTable.X, onTable.Z));
    }

    [Fact]
    public void AWallCantBePicked()
    {
        var room = new SimulatedTable();
        Assert.Null(PickAt(room, 0f, 0.3f, room.WallDistance));
    }

    [Fact]
    public void NothingThereNothingPicked()
    {
        var frame = new DepthFrame(Width, Height);
        for (var i = 0; i < frame.Points.Length; i++) frame.Points[i] = DepthFrame.Invalid;
        Assert.Null(SurfacePicker.Pick(frame, 256, 212));
    }

    [Fact]
    public void TheBoardSitsWhereItsPutAndTurns()
    {
        var room = new SimulatedTable();
        var board = new Board(PickAt(room, 0f, 0f, 0.6f));
        Assert.Equal(1f, board.Coverage(), 2);
        var middle = In(board.Frame, room, 0f, 0f, 0.6f);
        Assert.InRange(middle.Length(), 0f, 0.02f);
        // Slid well off the table's side, it hangs off.
        board.Move(0.6f, 0f, 0f);
        Assert.InRange(board.Coverage(), 0f, 0.7f);
        board.Move(-0.6f, 0f, (float)(Math.PI / 2));
        // Turned a quarter, the board's long side runs away from the Kinect.
        var along = In(board.Frame, room, 0f, 0f, 0.8f) - In(board.Frame, room, 0f, 0f, 0.6f);
        Assert.True(Math.Abs(along.X) > 0.19f && Math.Abs(along.Z) < 0.01f, $"{along}");
    }

    private static BoardTracker Play(SimulatedTable room, Action<DepthFrame> change = null)
    {
        var board = new Board(PickAt(room, 0f, 0f, 0.6f));
        var tracker = new BoardTracker(board);
        var random = new Random(3);
        for (var i = 0; i < 12; i++)
        {
            var frame = room.Render(Width, Height, random);
            change?.Invoke(frame);
            tracker.Update(frame, 2);
        }
        return tracker;
    }

    private static void Watch(BoardTracker tracker, SimulatedTable room, int frames, Action<DepthFrame> change = null)
    {
        var random = new Random(9);
        for (var i = 0; i < frames; i++)
        {
            var frame = room.Render(Width, Height, random);
            change?.Invoke(frame);
            tracker.Update(frame, 2);
        }
    }

    [Fact]
    public void ACupOnTheBoardBlocksAndClearsWhenRemoved()
    {
        var room = new SimulatedTable();
        var tracker = Play(room);
        Assert.True(tracker.IsReady);
        room.Objects.Add(Box.OnTable(0.15f, 0.6f, 0.08f, 0.08f, 0.1f));
        Watch(tracker, room, 4);
        var cup = In(tracker.Board.Frame, room, 0.15f, 0f, 0.6f);
        Assert.True(tracker.Obstacles.IsBlocked(cup.X, cup.Z));
        Assert.False(tracker.PlayArea().Contains(cup.X, cup.Z));
        var open = In(tracker.Board.Frame, room, -0.15f, 0f, 0.6f);
        Assert.True(tracker.PlayArea().Contains(open.X, open.Z));
        room.Objects.Clear();
        Watch(tracker, room, 4);
        Assert.False(tracker.Obstacles.IsBlocked(cup.X, cup.Z));
    }

    [Fact]
    public void AFlatBookCountsButAHoveringHandDoesNot()
    {
        var room = new SimulatedTable();
        var tracker = Play(room);
        room.Objects.Add(Box.OnTable(-0.15f, 0.55f, 0.12f, 0.15f, 0.03f));
        room.Hand = new Vector3(0.15f, 0.25f, 0.65f);
        Watch(tracker, room, 4);
        var book = In(tracker.Board.Frame, room, -0.15f, 0f, 0.55f);
        var underHand = In(tracker.Board.Frame, room, 0.15f, 0f, 0.65f);
        Assert.True(tracker.Obstacles.IsBlocked(book.X, book.Z));
        Assert.False(tracker.Obstacles.IsBlocked(underHand.X, underHand.Z));
    }

    [Fact]
    public void ASurfaceMeasuredSlightlyWrongHasNoPhantomObjects()
    {
        // From low down the Kinect reads the back of a table as rising; here the depth of the
        // far part is pulled in along each ray.
        var room = new SimulatedTable { PitchDegrees = 15f, SensorPosition = new Vector3(0f, 0.25f, -0.1f), TableNear = 0.45f, TableDepth = 0.5f, Noise = 0.004f };
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
        var board = new Board(PickAt(room, 0f, 0f, 0.6f, Render(room, Distort)));
        var tracker = new BoardTracker(board);
        Watch(tracker, room, 12, Distort);
        var blocked = 0;
        for (var cell = 0; cell < tracker.Obstacles.Grid.CellCount; cell++)
        {
            if (tracker.Obstacles.IsBlocked(cell)) blocked++;
        }
        Assert.Equal(0, blocked);
    }

    private static DepthFrame Render(SimulatedTable room, Action<DepthFrame> change)
    {
        var frame = room.Render(Width, Height, new Random(1));
        change(frame);
        return frame;
    }

    [Fact]
    public void BoardCoordinatesAreNotMirrored()
    {
        var room = new SimulatedTable();
        var board = new Board(PickAt(room, 0f, 0f, 0.6f));
        var left = In(board.Frame, room, -0.2f, 0f, 0.6f);
        var right = In(board.Frame, room, 0.2f, 0f, 0.6f);
        var far = In(board.Frame, room, 0f, 0f, 0.8f);
        var near = In(board.Frame, room, 0f, 0f, 0.5f);
        Assert.Equal(0.4f, right.X - left.X, 2);
        Assert.Equal(0.3f, far.Z - near.Z, 2);
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

public class BoardFitTests
{
    [Fact]
    public void ABoardTooBigForTheSurfaceShrinksAndSlidesOntoIt()
    {
        // A desk only 30 cm deep in view, picked near its front edge.
        var room = new SimulatedTable { TableDepth = 0.3f, TableNear = 0.4f, WallDistance = 0.75f };
        var frame = room.Render(512, 424, new Random(1));
        var c = room.RoomToCamera(new Vector3(0.1f, 0f, 0.42f));
        var focal = 365f;
        var surface = SurfacePicker.Pick(frame, (int)(256 - focal * c.X / c.Z), (int)(212 - focal * c.Y / c.Z));
        Assert.NotNull(surface);
        var board = new Board(surface);
        Assert.True(board.Coverage() >= 0.95f, $"coverage {board.Coverage()}");
        Assert.True(board.Length <= 0.33f, $"length {board.Length}");
        Assert.True(board.Scale >= Board.MinScale);
    }
}

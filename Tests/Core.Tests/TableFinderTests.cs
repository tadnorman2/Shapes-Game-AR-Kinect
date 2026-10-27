using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using ShapesKinect.Core;
using Xunit;

public class TableFinderTests
{
    /// <summary>Finds the table with the Kinect placed as given, at full Kinect resolution, and checks it's the table.</summary>
    private static void FindsTheTable(SimulatedTable room, bool withHint)
    {
        var tracker = new TableTracker();
        var report = new List<string>();
        var frame = room.Render(512, 424, new Random(1));
        if (!withHint) frame.UpHint = null;
        Assert.True(tracker.Calibrate(frame, report), string.Join("\n", report));
        Assert.Equal(room.SensorPosition.Y, tracker.Surface.Frame.CameraPosition.Y, 1);
        var middle = tracker.ToTable(room.RoomToCamera(new Vector3(0f, 0f, room.TableNear + room.TableDepth / 2)));
        Assert.True(tracker.Surface.Contains(middle.X, middle.Z), string.Join("\n", report));
        Assert.InRange(tracker.Surface.Area, 0.1f, room.TableWidth * room.TableDepth * 1.25f);
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
            FindsTheTable(new SimulatedTable
            {
                PitchDegrees = pitch, SensorPosition = new Vector3(0f, height, -0.1f), TableNear = near,
                TableWidth = 1.0f, TableDepth = 0.6f, Noise = 0.004f,
            }, withHint);
        }
    }

    [Fact]
    public void FindsAShallowDeskInFrontOfAWall()
    {
        // Like a real capture: the Kinect 20 cm above a desk, tilted 12 degrees down, with the
        // wall 30 cm past the desk's front edge filling most of the picture.
        var room = new SimulatedTable
        {
            PitchDegrees = 12f, SensorPosition = new Vector3(0f, 0.2f, -0.1f), TableNear = 0.45f,
            TableDepth = 0.3f, TableWidth = 1.4f, WallDistance = 0.75f, Noise = 0.004f,
        };
        var tracker = new TableTracker();
        Assert.True(tracker.Calibrate(room.Render(512, 424, new Random(1))));
        Assert.Equal(0.2f, tracker.Surface.Frame.CameraPosition.Y, 2);
        var (width, depth) = tracker.Surface.Extent;
        Assert.InRange(depth, 0.15f, 0.35f);
        Assert.InRange(width, 0.5f, 1.45f);
    }

    [Fact]
    public void AnEmptyHintFromTheKinectIsIgnored()
    {
        var room = new SimulatedTable();
        var frame = room.Render(512, 424, new Random(1));
        frame.UpHint = Vector3.Zero;
        var tracker = new TableTracker();
        Assert.True(tracker.Calibrate(frame));
        Assert.Equal(room.SensorPosition.Y, tracker.Surface.Frame.CameraPosition.Y, 2);
    }

    [Fact]
    public void AWallIsNotATable()
    {
        // Pointed level at the wall, too high to see the table or floor.
        var room = new SimulatedTable { PitchDegrees = 0f, SensorPosition = new Vector3(0f, 1.5f, -0.1f) };
        var report = new List<string>();
        Assert.False(new TableTracker().Calibrate(room.Render(512, 424, new Random(1)), report), string.Join("\n", report));
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

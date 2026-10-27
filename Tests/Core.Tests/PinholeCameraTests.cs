using System;
using System.Collections.Generic;
using System.Numerics;
using ShapesKinect.Core;
using Xunit;

public class PinholeCameraTests
{
    /// <summary>A camera like the Kinect's color camera: 1920 x 1080, 5 cm to the side, turned slightly.</summary>
    private static PinholeCamera KinectLike(bool mirrored)
    {
        var turn = Quaternion.CreateFromYawPitchRoll(0.02f, -0.01f, 0.005f);
        var right = Vector3.Transform(new Vector3(1, 0, 0), turn);
        var down = Vector3.Transform(new Vector3(0, -1, 0), turn);
        var forward = Vector3.Transform(new Vector3(0, 0, 1), turn);
        return new PinholeCamera(new Vector3(-0.052f, 0.001f, 0.002f), mirrored ? -right : right, down, forward,
            1060f, 1058f, 955f, 542f, 1920, 1080);
    }

    /// <summary>Points spread through the camera's view between 0.5 and 4 m, with their pixels.</summary>
    private static (List<Vector3> points, List<Vector2> pixels) Sample(PinholeCamera camera, float noise = 0f)
    {
        var random = new Random(9);
        var points = new List<Vector3>();
        var pixels = new List<Vector2>();
        while (points.Count < 400)
        {
            var depth = 0.5f + (float)random.NextDouble() * 3.5f;
            var p = camera.Position + camera.Forward * depth +
                camera.Right * (float)((random.NextDouble() - 0.5) * depth) +
                camera.Down * (float)((random.NextDouble() - 0.5) * depth * 0.6);
            var pixel = camera.Project(p);
            if (pixel.X < 0 || pixel.X > 1920 || pixel.Y < 0 || pixel.Y > 1080) continue;
            points.Add(p);
            pixels.Add(pixel + new Vector2((float)(random.NextDouble() - 0.5) * noise, (float)(random.NextDouble() - 0.5) * noise));
        }
        return (points, pixels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoversTheCameraFromItsProjections(bool mirrored)
    {
        var truth = KinectLike(mirrored);
        var (points, pixels) = Sample(truth);
        var fitted = PinholeCamera.Fit(points, pixels, 1920, 1080);
        Assert.NotNull(fitted);
        Assert.True(Vector3.Distance(truth.Position, fitted.Position) < 0.001f, $"position {fitted.Position}");
        Assert.True(Vector3.Dot(truth.Right, fitted.Right) > 0.9999f);
        Assert.True(Vector3.Dot(truth.Down, fitted.Down) > 0.9999f);
        Assert.True(Vector3.Dot(truth.Forward, fitted.Forward) > 0.9999f);
        Assert.Equal(truth.Fx, fitted.Fx, 0);
        Assert.Equal(truth.Fy, fitted.Fy, 0);
        Assert.Equal(truth.Cx, fitted.Cx, 0);
        Assert.Equal(truth.Cy, fitted.Cy, 0);
    }

    [Fact]
    public void FitIsCloseDespiteNoisyPixels()
    {
        var truth = KinectLike(false);
        var (points, pixels) = Sample(truth, noise: 1f);
        var fitted = PinholeCamera.Fit(points, pixels, 1920, 1080);
        var worst = 0f;
        for (var i = 0; i < points.Count; i++) worst = Math.Max(worst, Vector2.Distance(truth.Project(points[i]), fitted.Project(points[i])));
        Assert.True(worst < 1.5f, $"worst error {worst} px");
    }

    [Fact]
    public void MovingToTableCoordinatesKeepsProjectionsTheSame()
    {
        var camera = KinectLike(false);
        var frame = new TableFrame(new Vector3(0f, -0.5f, 1f), new Vector3(0f, 0.8f, -0.6f));
        var onTable = camera.Transformed(frame.ToTable, frame.DirectionToTable);
        var point = new Vector3(0.1f, -0.2f, 1.2f);
        Assert.True(Vector2.Distance(camera.Project(point), onTable.Project(frame.ToTable(point))) < 0.01f);
    }

    [Fact]
    public void TooFewPointsGiveNoCamera()
    {
        Assert.Null(PinholeCamera.Fit(new List<Vector3> { Vector3.One }, new List<Vector2> { Vector2.One }, 1920, 1080));
    }
}

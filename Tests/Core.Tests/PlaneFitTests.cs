using System;
using System.Collections.Generic;
using System.Numerics;
using ShapesKinect.Core;
using Xunit;

public class PlaneFitTests
{
    [Fact]
    public void SmallestEigenvectorOfADiagonalMatrix()
    {
        var v = PlaneFit.SmallestEigenvector(new double[,] { { 3, 0, 0 }, { 0, 1, 0 }, { 0, 0, 2 } });
        Assert.Equal(1f, Math.Abs(v.Y), 4);
    }

    [Fact]
    public void AWallFacingTheCameraIsNotTakenForATable()
    {
        // A wall 1.5 m ahead, seen by a camera tilted 45 degrees down: in camera space its normal is
        // as far from "up" as a table's would be, but it faces the camera from the front.
        var random = new Random(3);
        var pitch = Math.PI / 4;
        var wallNormalRoom = new Vector3(0f, 0f, -1f);
        var up = new Vector3(0f, (float)Math.Cos(pitch), (float)Math.Sin(pitch));
        var forward = new Vector3(0f, (float)-Math.Sin(pitch), (float)Math.Cos(pitch));
        Vector3 ToCamera(Vector3 room) => new Vector3(-room.X, Vector3.Dot(room, up), Vector3.Dot(room, forward));
        var points = new List<Vector3>();
        for (var i = 0; i < 2000; i++)
        {
            var room = new Vector3((float)(random.NextDouble() - 0.5), (float)(random.NextDouble() - 0.5), 1.5f);
            points.Add(ToCamera(room));
        }
        Assert.True(Vector3.Dot(ToCamera(wallNormalRoom), up) < 1f);
        Assert.Null(PlaneFit.FindUpFacing(points, new Vector3(0f, 1f, 0f)));
    }

    [Fact]
    public void FindsATiltedPlaneAmongClutter()
    {
        var random = new Random(2);
        var normal = Vector3.Normalize(new Vector3(0f, 1f, 0.5f));
        var along = Vector3.Normalize(Vector3.Cross(normal, new Vector3(1f, 0f, 0f)));
        var points = new List<Vector3>();
        for (var i = 0; i < 2000; i++)
        {
            var p = new Vector3(0f, -0.8f, 0.6f) + new Vector3(1f, 0f, 0f) * (float)(random.NextDouble() - 0.5) +
                along * (float)(random.NextDouble() - 0.5) + normal * (float)((random.NextDouble() - 0.5) * 0.004);
            points.Add(p);
        }
        for (var i = 0; i < 1000; i++)
        {
            points.Add(new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble() + 1f));
        }
        var plane = PlaneFit.FindUpFacing(points, new Vector3(0f, 1f, 0f));
        // Found, with its normal on the camera's side: the camera is above and in front of it.
        Assert.NotNull(plane);
        Assert.True(Vector3.Dot(plane.Value.Normal, normal) > 0.999f);
        Assert.True(Math.Abs(plane.Value.SignedDistance(new Vector3(0f, -0.8f, 0.6f))) < 0.003f);
    }
}

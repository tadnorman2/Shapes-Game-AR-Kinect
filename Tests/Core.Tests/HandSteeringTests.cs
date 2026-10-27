using System.Numerics;
using ShapesKinect.Core;
using Xunit;

public class HandSteeringTests
{
    [Fact]
    public void BallHeadsForAHandHeldOverTheTable()
    {
        var move = HandSteering.Steer(0f, 0f, new Vector3(0.5f, 0.3f, 0f));
        Assert.Equal(1f, move.X, 3);
        Assert.Equal(0f, move.Y, 3);
    }

    [Fact]
    public void BallSlowsAsItCatchesUpAndStopsUnderTheHand()
    {
        var close = HandSteering.Steer(0f, 0f, new Vector3(0.05f, 0.3f, 0f));
        Assert.InRange(close.Length(), 0.1f, 0.5f);
        Assert.Equal(Vector2.Zero, HandSteering.Steer(0f, 0f, new Vector3(0.01f, 0.3f, 0f)));
    }

    [Fact]
    public void AHandRestingOnTheTableOrRaisedHighDoesNotSteer()
    {
        Assert.Equal(Vector2.Zero, HandSteering.Steer(0f, 0f, new Vector3(0.5f, 0.05f, 0f)));
        Assert.Equal(Vector2.Zero, HandSteering.Steer(0f, 0f, new Vector3(0.5f, 0.9f, 0f)));
    }
}

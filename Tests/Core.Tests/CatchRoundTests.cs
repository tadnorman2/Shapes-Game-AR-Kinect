using System;
using System.Collections.Generic;
using System.Numerics;
using ShapesKinect.Core;
using Xunit;

public class CatchRoundTests
{
    /// <summary>A person standing 2 m from the Kinect, arms out, hands and feet seen.</summary>
    private static Body Person(float x = 0f, Vector3? rightHand = null)
    {
        var body = new Body();
        body.Set(BodyJoint.Head, new Vector3(x, 0.6f, 2f), true);
        body.Set(BodyJoint.SpineShoulder, new Vector3(x, 0.4f, 2f), true);
        body.Set(BodyJoint.SpineMid, new Vector3(x, 0.15f, 2f), true);
        body.Set(BodyJoint.SpineBase, new Vector3(x, -0.1f, 2f), true);
        body.Set(BodyJoint.HandLeft, new Vector3(x + 0.6f, 0.3f, 1.95f), true);
        body.Set(BodyJoint.HandRight, rightHand ?? new Vector3(x - 0.6f, 0.3f, 1.95f), true);
        body.Set(BodyJoint.FootLeft, new Vector3(x + 0.15f, -0.85f, 2f), true);
        body.Set(BodyJoint.FootRight, new Vector3(x - 0.15f, -0.85f, 2f), true);
        return body;
    }

    [Fact]
    public void ShapesAppearWithinReachInFrontOfThePlayer()
    {
        var round = new CatchRound(random: new Random(1));
        var bodies = new List<Body> { Person() };
        for (var i = 0; i < 100; i++) round.Update(0.1f, bodies);
        Assert.NotEmpty(round.Shapes);
        foreach (var shape in round.Shapes)
        {
            var off = shape.Position - new Vector3(0f, 0.15f, 2f);
            Assert.InRange(Math.Abs(off.X), 0f, 0.9f);
            Assert.InRange(off.Y, -0.7f, 0.8f);
            // In front of the player: nearer the Kinect.
            Assert.InRange(off.Z, -0.5f, 0f);
        }
    }

    [Fact]
    public void TouchingAShapeCatchesIt()
    {
        var round = new CatchRound(random: new Random(1));
        var spot = new Vector3(-0.3f, 0.5f, 1.8f);
        round.Place(ShapeType.Star, spot);
        var events = round.Update(0.02f, new List<Body> { Person(rightHand: spot + new Vector3(0.05f, 0f, 0f)) });
        var caught = Assert.Single(events.FindAll(e => e is RoundEvent.Collected));
        Assert.Equal(10, ((RoundEvent.Collected)caught).Points);
        Assert.Equal(10, round.Score);
    }

    [Fact]
    public void AGuessedHandDoesntCatch()
    {
        var round = new CatchRound(random: new Random(1));
        var spot = new Vector3(-0.3f, 0.5f, 1.8f);
        round.Place(ShapeType.Star, spot);
        var body = Person();
        body.Set(BodyJoint.HandRight, spot, false);
        round.Update(0.02f, new List<Body> { body });
        Assert.Equal(0, round.Score);
    }

    [Fact]
    public void AfterTheFreeStretchTheWrongShapeCosts()
    {
        var round = new CatchRound(random: new Random(2));
        var bodies = new List<Body> { Person() };
        while (round.Target == null) round.Update(0.1f, bodies);
        var wrong = ShapeType.Cube == round.Target ? ShapeType.Star : ShapeType.Cube;
        var spot = new Vector3(-0.3f, 0.5f, 1.8f);
        round.Place(wrong, spot);
        round.Update(0.02f, new List<Body> { Person(rightHand: spot) });
        Assert.Equal(0, round.Score);
        round.Place(round.Target.Value, spot);
        round.Update(0.02f, new List<Body> { Person(rightHand: spot) });
        Assert.Equal(10, round.Score);
    }

    [Fact]
    public void TheCalledShapeIsAlwaysOut()
    {
        var round = new CatchRound(random: new Random(3));
        var bodies = new List<Body> { Person() };
        for (var i = 0; i < 400; i++)
        {
            var events = round.Update(0.1f, bodies);
            if (events.Exists(e => e is RoundEvent.TargetChanged)) Assert.Contains(round.Shapes, s => s.Type == round.Target);
        }
    }

    [Fact]
    public void UncaughtShapesFadeAway()
    {
        var rules = new CatchRules { SpawnInterval = 1000f };
        var round = new CatchRound(rules, new Random(1));
        round.Place(ShapeType.Cube, new Vector3(0f, 1.5f, 1.5f));
        var bodies = new List<Body> { Person() };
        for (var i = 0; i < 80; i++) round.Update(0.1f, bodies);
        Assert.Empty(round.Shapes);
    }

    [Fact]
    public void TheClockStopsWithNobodyInView()
    {
        var round = new CatchRound(random: new Random(1));
        round.Update(1f, new List<Body>());
        Assert.False(round.HasPlayer);
        Assert.Equal(0f, round.Elapsed);
        round.Update(1f, new List<Body> { Person() });
        Assert.True(round.HasPlayer);
        Assert.Equal(1f, round.Elapsed);
    }

    [Fact]
    public void ShapesAppearAroundWhoeverIsInTheMiddle()
    {
        var round = new CatchRound(random: new Random(4));
        var bodies = new List<Body> { Person(1.2f), Person(0.1f) };
        for (var i = 0; i < 60; i++) round.Update(0.1f, bodies);
        // Within reach of them, give or take how far shapes drift.
        foreach (var shape in round.Shapes) Assert.InRange(shape.Position.X, 0.1f - 1.1f, 0.1f + 1.1f);
    }

    [Fact]
    public void TheRoundEnds()
    {
        var round = new CatchRound(new CatchRules { RoundSeconds = 5f }, new Random(1));
        var bodies = new List<Body> { Person() };
        var finished = false;
        for (var i = 0; i < 60 && !finished; i++) finished = round.Update(0.1f, bodies).Exists(e => e is RoundEvent.Finished);
        Assert.True(finished);
        Assert.True(round.IsFinished);
    }
}

using System;
using System.Linq;
using ShapesKinect.Core;
using Xunit;

public class GameRoundTests
{
    /// <summary>A 60 cm square of table centered on the start point.</summary>
    private static readonly IPlayArea Table = new PlayArea((x, z) => Math.Abs(x) <= 0.3f && Math.Abs(z) <= 0.3f);

    private static GameRound NewRound(IPlayArea area = null) => new GameRound(area ?? Table, new RoundRules(), new Random(42));

    private static void Collect(GameRound round, ShapeType type)
    {
        round.Place(type, round.PlayerX, round.PlayerZ);
        round.Update(0.001f, 0f, 0f);
    }

    private static void Hold(GameRound round, float moveX, float moveZ, float seconds)
    {
        for (var i = 0; i < (int)(seconds * 60); i++) round.Update(1f / 60, moveX, moveZ);
    }

    [Fact]
    public void AnyShapeScoresBeforeTheFirstTarget()
    {
        var round = NewRound();
        Assert.Null(round.Target);
        Collect(round, ShapeType.Star);
        Collect(round, ShapeType.Cube);
        Assert.Equal(20, round.Score);
    }

    [Fact]
    public void OnlyTheTargetScoresAndOthersCostPoints()
    {
        var round = NewRound();
        round.Update(round.Rules.FreeSeconds, 0f, 0f);
        var target = round.Target.Value;
        var other = ShapeTypes.All.First(t => t != target);
        Collect(round, target);
        Collect(round, target);
        Assert.Equal(20, round.Score);
        Collect(round, other);
        Assert.Equal(15, round.Score);
    }

    [Fact]
    public void ScoreNeverGoesBelowZero()
    {
        var round = NewRound();
        round.Update(round.Rules.FreeSeconds, 0f, 0f);
        Collect(round, ShapeTypes.All.First(t => t != round.Target));
        Assert.Equal(0, round.Score);
    }

    [Fact]
    public void TargetKeepsChanging()
    {
        var round = NewRound();
        round.Update(round.Rules.FreeSeconds, 0f, 0f);
        var previous = round.Target;
        for (var i = 0; i < 5; i++)
        {
            round.Update(round.Rules.TargetSeconds, 0f, 0f);
            Assert.NotEqual(previous, round.Target);
            previous = round.Target;
        }
    }

    [Fact]
    public void RoundEndsOnceAfterThreeMinutes()
    {
        var round = NewRound();
        var finished = 0;
        for (var i = 0; i < 181; i++)
        {
            if (round.Update(1f, 0f, 0f).Any(e => e is RoundEvent.Finished)) finished++;
        }
        Assert.True(round.IsFinished);
        Assert.Equal(1, finished);
    }

    [Fact]
    public void BallSlidesAlongEdgesAndStaysOnTheTable()
    {
        var round = NewRound();
        Hold(round, 1f, 0f, 3f);
        var edgeX = round.PlayerX;
        Assert.InRange(edgeX, 0.26f, 0.27f);
        Hold(round, 1f, 1f, 0.5f);
        Assert.Equal(edgeX, round.PlayerX, 5);
        Assert.True(round.PlayerZ > 0.05f);
    }

    [Fact]
    public void ShapesCoveredByAnObjectDisappear()
    {
        var covered = false;
        var round = NewRound(new PlayArea((x, z) => Table.Contains(x, z) && !(covered && x > 0.15f)));
        round.Place(ShapeType.Star, 0.2f, 0f);
        var star = round.Shapes.Last();
        covered = true;
        Hold(round, 0f, 0f, 1.2f);
        Assert.DoesNotContain(star, round.Shapes);
    }

    [Fact]
    public void ShapesOnlyAppearOnTheTable()
    {
        var round = NewRound();
        for (var i = 0; i < 100; i++) round.Update(1f, 0f, 0f);
        Assert.NotEmpty(round.Shapes);
        Assert.All(round.Shapes, s => Assert.True(Table.Contains(s.X, s.Z)));
        Assert.True(round.Shapes.Count <= round.Rules.MaxShapes);
    }
}

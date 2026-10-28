using ShapesKinect.Core;
using Xunit;

public class CardCodesTests
{
    [Fact]
    public void CardsCantBeMistakenForEachOtherOrThemselvesTurned()
    {
        for (var a = 0; a < CardCodes.Codes.Length; a++)
        {
            for (var b = 0; b < CardCodes.Codes.Length; b++)
            {
                foreach (var mirrored in new[] { false, true })
                {
                    for (var turns = 0; turns < 4; turns++)
                    {
                        if (a == b && !mirrored && turns == 0) continue;
                        var difference = CardCodes.BitCount(CardCodes.Codes[a] ^ CardCodes.Transform(CardCodes.Codes[b], mirrored, turns));
                        Assert.True(difference >= 2 * CardCodes.MaxErrors + 1, $"card {a} vs {b} mirrored {mirrored} turned {turns}: {difference}");
                    }
                }
            }
        }
    }

    [Fact]
    public void TransformingPointsMatchesTransformingCells()
    {
        // The inner cell at row 1, column 2 has its middle at (3.5, 2.5) in cells, border included.
        foreach (var mirrored in new[] { false, true })
        {
            for (var turns = 0; turns < 4; turns++)
            {
                var moved = CardCodes.Transform(1 << (1 * 4 + 2), mirrored, turns);
                var (u, v) = CardCodes.TransformPoint(3.5f, 2.5f, mirrored, turns);
                var row = (int)v - 1;
                var column = (int)u - 1;
                Assert.Equal(1 << (row * 4 + column), moved);
            }
        }
    }

    [Fact]
    public void ReadsACardWithTwoCellsWrong()
    {
        var seen = CardCodes.Transform(CardCodes.Codes[2], true, 3) ^ 0b1000_0000_0010_0000;
        Assert.Equal((2, true, 3), CardCodes.Match(seen));
    }
}

using System;

namespace ShapesKinect.Core
{
    /// <summary>
    /// The printed cards' patterns. Each card is a black square of 6 by 6 cells: a one-cell black
    /// border around 4 by 4 cells, each black or white. Bit (row * 4 + column) of a code is the
    /// inner cell at that row and column, counted from the top left as printed; 1 is black.
    ///
    /// The codes were picked so that every card differs from every other in at least 5 cells,
    /// however either is turned or mirrored, and from itself turned or mirrored too. So a card is
    /// still read right with two cells misread, its turn is never in doubt, and a mirrored camera
    /// picture (the Kinect's is) reads fine.
    /// </summary>
    public static class CardCodes
    {
        public static readonly ushort[] Codes = { 0xca26, 0x442f, 0x49db, 0x932a, 0xc8e5, 0x6a78 };

        /// <summary>Cells per side, border included.</summary>
        public const int Cells = 6;

        /// <summary>A code read with up to this many cells wrong still counts.</summary>
        public const int MaxErrors = 2;

        /// <summary>Whether the inner cell at <paramref name="row"/>, <paramref name="column"/> of card <paramref name="id"/> is black, as printed.</summary>
        public static bool IsBlack(int id, int row, int column) => (Codes[id] >> (row * 4 + column) & 1) == 1;

        /// <summary>
        /// The code turned <paramref name="turns"/> quarter turns clockwise, after mirroring left
        /// to right first if <paramref name="mirrored"/>: what's read off a card seen that way.
        /// </summary>
        public static int Transform(int code, bool mirrored, int turns)
        {
            var bits = code;
            if (mirrored) bits = Map(bits, (row, column) => (row, 3 - column));
            for (var i = 0; i < turns; i++) bits = Map(bits, (row, column) => (column, 3 - row));
            return bits;
        }

        /// <summary>Moves each bit from (row, column) to where <paramref name="to"/> says.</summary>
        private static int Map(int bits, Func<int, int, (int row, int column)> to)
        {
            var result = 0;
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    if ((bits >> (row * 4 + column) & 1) == 0) continue;
                    var (r, c) = to(row, column);
                    result |= 1 << (r * 4 + c);
                }
            }
            return result;
        }

        /// <summary>
        /// Where a point at (<paramref name="u"/>, <paramref name="v"/>) on the printed card, in
        /// cells from its top left (0 to 6 each way), ends up on the card seen mirrored and turned
        /// as in <see cref="Transform"/>.
        /// </summary>
        public static (float u, float v) TransformPoint(float u, float v, bool mirrored, int turns)
        {
            if (mirrored) u = Cells - u;
            for (var i = 0; i < turns; i++) (u, v) = (Cells - v, u);
            return (u, v);
        }

        /// <summary>
        /// Which card <paramref name="seen"/> is, and how it's mirrored and turned, if it's within
        /// <see cref="MaxErrors"/> cells of one.
        /// </summary>
        public static (int id, bool mirrored, int turns)? Match(int seen)
        {
            for (var id = 0; id < Codes.Length; id++)
            {
                foreach (var mirrored in new[] { false, true })
                {
                    for (var turns = 0; turns < 4; turns++)
                    {
                        if (BitCount(Transform(Codes[id], mirrored, turns) ^ seen) <= MaxErrors) return (id, mirrored, turns);
                    }
                }
            }
            return null;
        }

        public static int BitCount(int bits)
        {
            var count = 0;
            for (; bits != 0; bits &= bits - 1) count++;
            return count;
        }
    }
}

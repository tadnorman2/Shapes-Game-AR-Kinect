using System.Collections.Generic;

namespace ShapesKinect.Core
{
    /// <summary>A wall inside an arena: a rectangle from (X1, Z1) to (X2, Z2), in meters from the arena's middle.</summary>
    public readonly struct Wall
    {
        public float X1 { get; }
        public float Z1 { get; }
        public float X2 { get; }
        public float Z2 { get; }

        public Wall(float x1, float z1, float x2, float z2)
        {
            X1 = x1;
            Z1 = z1;
            X2 = x2;
            Z2 = z2;
        }

        public bool Contains(float x, float z) => x >= X1 && x <= X2 && z >= Z1 && z <= Z2;
    }

    /// <summary>
    /// An arena the game builds around a card: its size and the walls inside it. Each card brings
    /// up its own. Arenas are laid out with X across and Z along, the card in the middle and its
    /// printed top pointing along +Z.
    /// </summary>
    public sealed class ArenaLayout
    {
        public string Name { get; }
        public float Width { get; }
        public float Length { get; }
        public IReadOnlyList<Wall> Walls { get; }

        public ArenaLayout(string name, float width, float length, IReadOnlyList<Wall> walls)
        {
            Name = name;
            Width = width;
            Length = length;
            Walls = walls;
        }

        /// <summary>The arena card <paramref name="id"/> brings up; cards past the last arena reuse them in turn.</summary>
        public static ArenaLayout ForCard(int id) => All[id % All.Length];

        public static readonly ArenaLayout[] All =
        {
            new ArenaLayout("Open field", 0.6f, 0.4f, new Wall[0]),
            new ArenaLayout("Pillars", 0.6f, 0.4f, new[]
            {
                new Wall(-0.19f, -0.12f, -0.13f, -0.06f),
                new Wall(0.13f, -0.12f, 0.19f, -0.06f),
                new Wall(-0.19f, 0.06f, -0.13f, 0.12f),
                new Wall(0.13f, 0.06f, 0.19f, 0.12f),
            }),
            new ArenaLayout("Zigzag", 0.6f, 0.4f, new[]
            {
                new Wall(-0.3f, -0.08f, 0.1f, -0.06f),
                new Wall(-0.1f, 0.06f, 0.3f, 0.08f),
            }),
        };
    }
}

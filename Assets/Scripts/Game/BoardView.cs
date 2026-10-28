using System.Collections.Generic;
using ShapesKinect.Core;
using UnityEngine;

namespace ShapesKinect.Game
{
    /// <summary>
    /// Draws the game's own arena: a see-through tinted floor with low glowing walls around it,
    /// and the arena's own walls inside, in the board's coordinates (the middle at the origin,
    /// lying on Y = 0).
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        private Mesh mesh;
        private ArenaLayout shown;

        /// <summary>How high the walls around the edge stand, and the ones inside, in meters.</summary>
        private const float WallHeight = 0.02f;
        private const float InnerWallHeight = 0.035f;
        private const float WallThickness = 0.008f;

        private static readonly Color Tint = new Color(0.35f, 0.85f, 1f);
        private static readonly Color InnerWall = new Color(0.55f, 0.6f, 1f);

        private void Awake()
        {
            mesh = new Mesh { name = "Board" };
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = Materials.VertexColor();
        }

        /// <summary>Shows the arena <paramref name="layout"/>.</summary>
        public void Show(ArenaLayout layout)
        {
            gameObject.SetActive(true);
            if (layout == shown) return;
            shown = layout;

            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            float w = layout.Width / 2, l = layout.Length / 2;
            // The floor, faint, just above the surface so it doesn't flicker into it.
            Quad(vertices, colors, triangles, new Vector3(-w, 0.001f, -l), new Vector3(w, 0.001f, -l),
                new Vector3(w, 0.001f, l), new Vector3(-w, 0.001f, l), new Color(Tint.r, Tint.g, Tint.b, 0.18f));
            // Four walls around the edge, each a thin box.
            var wall = new Color(Tint.r, Tint.g, Tint.b, 0.85f);
            Box(vertices, colors, triangles, new Vector3(-w - WallThickness, 0f, -l - WallThickness), new Vector3(w + WallThickness, WallHeight, -l), wall);
            Box(vertices, colors, triangles, new Vector3(-w - WallThickness, 0f, l), new Vector3(w + WallThickness, WallHeight, l + WallThickness), wall);
            Box(vertices, colors, triangles, new Vector3(-w - WallThickness, 0f, -l), new Vector3(-w, WallHeight, l), wall);
            Box(vertices, colors, triangles, new Vector3(w, 0f, -l), new Vector3(w + WallThickness, WallHeight, l), wall);
            // The arena's own walls.
            foreach (var inner in layout.Walls)
            {
                Box(vertices, colors, triangles, new Vector3(inner.X1, 0f, inner.Z1), new Vector3(inner.X2, InnerWallHeight, inner.Z2), new Color(InnerWall.r, InnerWall.g, InnerWall.b, 0.9f));
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        public void Hide() => gameObject.SetActive(false);

        private static void Box(List<Vector3> v, List<Color> c, List<int> t, Vector3 min, Vector3 max, Color color)
        {
            Vector3 P(int x, int y, int z) => new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
            var top = color;
            var side = new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f, color.a);
            Quad(v, c, t, P(0, 1, 0), P(1, 1, 0), P(1, 1, 1), P(0, 1, 1), top);
            Quad(v, c, t, P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), side);
            Quad(v, c, t, P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1), side);
            Quad(v, c, t, P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0), side);
            Quad(v, c, t, P(1, 0, 0), P(1, 0, 1), P(1, 1, 1), P(1, 1, 0), side);
        }

        /// <summary>A four-cornered face, drawn from both sides.</summary>
        private static void Quad(List<Vector3> v, List<Color> c, List<int> t, Vector3 a, Vector3 b, Vector3 d, Vector3 e, Color color)
        {
            var start = v.Count;
            v.Add(a);
            v.Add(b);
            v.Add(d);
            v.Add(e);
            for (var i = 0; i < 4; i++) c.Add(color);
            t.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            t.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }
    }
}

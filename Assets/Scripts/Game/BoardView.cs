using System.Collections.Generic;
using UnityEngine;

namespace ShapesKinect.Game
{
    /// <summary>
    /// Draws the game's own arena: a see-through tinted floor with low glowing walls around it,
    /// in the board's own coordinates (the middle at the origin, lying on Y = 0).
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        private Mesh mesh;
        private float width = -1f;
        private float length = -1f;
        private Color color;

        /// <summary>How high the walls stand, in meters.</summary>
        private const float WallHeight = 0.02f;
        private const float WallThickness = 0.008f;

        public static readonly Color FitColor = new Color(0.35f, 0.85f, 1f);
        public static readonly Color OffColor = new Color(1f, 0.6f, 0.2f);

        private void Awake()
        {
            mesh = new Mesh { name = "Board" };
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = Materials.VertexColor();
        }

        /// <summary>Shows a board <paramref name="boardWidth"/> by <paramref name="boardLength"/> meters, in <paramref name="tint"/>.</summary>
        public void Show(float boardWidth, float boardLength, Color tint)
        {
            gameObject.SetActive(true);
            if (Mathf.Approximately(boardWidth, width) && Mathf.Approximately(boardLength, length) && tint == color) return;
            width = boardWidth;
            length = boardLength;
            color = tint;

            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            float w = width / 2, l = length / 2;
            // The floor, faint, just above the surface so it doesn't flicker into the depth.
            Quad(vertices, colors, triangles, new Vector3(-w, 0.001f, -l), new Vector3(w, 0.001f, -l),
                new Vector3(w, 0.001f, l), new Vector3(-w, 0.001f, l), new Color(tint.r, tint.g, tint.b, 0.18f));
            // Four walls, each a thin box.
            var wall = new Color(tint.r, tint.g, tint.b, 0.85f);
            Box(vertices, colors, triangles, new Vector3(-w - WallThickness, 0f, -l - WallThickness), new Vector3(w + WallThickness, WallHeight, -l), wall);
            Box(vertices, colors, triangles, new Vector3(-w - WallThickness, 0f, l), new Vector3(w + WallThickness, WallHeight, l + WallThickness), wall);
            Box(vertices, colors, triangles, new Vector3(-w - WallThickness, 0f, -l), new Vector3(-w, WallHeight, l), wall);
            Box(vertices, colors, triangles, new Vector3(w, 0f, -l), new Vector3(w + WallThickness, WallHeight, l), wall);

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

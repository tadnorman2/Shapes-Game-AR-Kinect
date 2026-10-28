using System.Collections.Generic;
using ShapesKinect.Core;
using UnityEngine;

namespace ShapesKinect.Game
{
    /// <summary>
    /// Shows what the Kinect's depth camera sees as a live 3D surface, in the camera's own view
    /// (unmirrored), colored by what each part is once there's a board: the board, things
    /// standing on it, a hand above it, and everything else around it.
    /// </summary>
    public sealed class TableView : MonoBehaviour
    {
        private Mesh mesh;
        private Vector3[] vertices = new Vector3[0];
        private Color32[] colors = new Color32[0];
        private bool[] valid = new bool[0];
        private float[] depths = new float[0];
        private readonly List<int> triangles = new List<int>();

        private static readonly Color32 TableColor = new Color32(96, 108, 128, 255);
        private static readonly Color32 ObjectColor = new Color32(240, 140, 60, 255);
        private static readonly Color32 HandColor = new Color32(170, 120, 235, 255);
        private static readonly Color32 SurroundColor = new Color32(52, 56, 66, 255);

        private void Awake()
        {
            mesh = new Mesh { name = "Kinect view" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = Materials.VertexColor();
        }

        /// <summary>
        /// Draws the surface invisibly, only hiding what's behind it, so the game's shapes go
        /// behind real people and things. Returns false if that can't be done.
        /// </summary>
        public bool UseAsMask()
        {
            var mask = Materials.DepthMask();
            if (mask == null) return false;
            GetComponent<MeshRenderer>().sharedMaterial = mask;
            return true;
        }

        /// <summary>
        /// Rebuilds the surface from <paramref name="frame"/>, colored by what's on
        /// <paramref name="board"/> (if there is one) as <paramref name="obstacles"/> sees it,
        /// and shaded by distance elsewhere.
        /// </summary>
        public void Show(DepthFrame frame, Board board, ObstacleMap obstacles)
        {
            // 256 columns looks smooth and is still quick to rebuild every frame.
            var step = Mathf.Max(1, frame.Width / 256);
            var columns = (frame.Width + step - 1) / step;
            var rows = (frame.Height + step - 1) / step;
            var count = columns * rows;
            if (vertices.Length != count)
            {
                vertices = new Vector3[count];
                colors = new Color32[count];
                valid = new bool[count];
                depths = new float[count];
                mesh.Clear();
            }

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var i = row * columns + column;
                    var p = frame[column * step, row * step];
                    valid[i] = DepthFrame.IsValid(p);
                    depths[i] = p.Z;
                    if (!valid[i])
                    {
                        vertices[i] = Vector3.zero;
                        continue;
                    }
                    // Camera space has X toward the sensor's left; flip it so the view isn't mirrored.
                    vertices[i] = new Vector3(-p.X, p.Y, p.Z);
                    colors[i] = board != null ? ColorFor(board.Frame.ToTable(p), board, obstacles) : Shade(p.Z);
                }
            }

            // Join neighboring readings into triangles, except across jumps in depth, so separate
            // things don't get webbed together.
            triangles.Clear();
            for (var row = 0; row + 1 < rows; row++)
            {
                for (var column = 0; column + 1 < columns; column++)
                {
                    var a = row * columns + column;
                    var b = a + 1;
                    var c = a + columns;
                    var d = c + 1;
                    if (!valid[a] || !valid[b] || !valid[c] || !valid[d]) continue;
                    var nearest = Mathf.Min(Mathf.Min(depths[a], depths[b]), Mathf.Min(depths[c], depths[d]));
                    var furthest = Mathf.Max(Mathf.Max(depths[a], depths[b]), Mathf.Max(depths[c], depths[d]));
                    if (furthest - nearest > 0.03f + 0.03f * nearest) continue;
                    // Both windings, so the surface shows from either side with any material.
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(d);
                    triangles.Add(c);
                }
            }

            mesh.SetTriangles(new int[0], 0);
            mesh.vertices = vertices;
            mesh.colors32 = colors;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private static Color32 ColorFor(System.Numerics.Vector3 t, Board board, ObstacleMap obstacles)
        {
            if (!board.Contains(t.X, t.Z) || t.Y < -0.05f) return SurroundColor;
            if (t.Y > HandSteering.MinHandHeight) return HandColor;
            if (obstacles != null ? obstacles.IsBlocked(t.X, t.Z) : t.Y >= ObstacleMap.MinHeight) return ObjectColor;
            return TableColor;
        }

        /// <summary>Lighter nearer.</summary>
        private static Color32 Shade(float depth)
        {
            var shade = (byte)Mathf.Clamp(230 - depth * 50f, 60, 230);
            return new Color32(shade, shade, (byte)Mathf.Min(255, shade + 20), 255);
        }
    }
}

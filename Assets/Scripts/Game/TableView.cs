using System.Collections.Generic;
using ShapesKinect.Core;
using UnityEngine;

namespace ShapesKinect.Game
{
    /// <summary>
    /// Shows what the Kinect sees as a live 3D surface. With a color camera, it's painted with the
    /// camera's picture, so it looks like the real room; otherwise, or when asked, it's colored by
    /// what each part is: the table, things standing on it, a hand above it, and everything else.
    /// </summary>
    public sealed class TableView : MonoBehaviour
    {
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private Material heightMaterial;
        private Material colorMaterial;
        private Vector3[] vertices = new Vector3[0];
        private Color32[] colors = new Color32[0];
        private Vector2[] uvs = new Vector2[0];
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
            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            heightMaterial = Materials.VertexColor();
            colorMaterial = Materials.UnlitTexture();
            meshRenderer.sharedMaterial = heightMaterial;
        }

        /// <summary>
        /// Rebuilds the surface from <paramref name="frame"/>. Once <paramref name="tracker"/> has
        /// found the table, the view is in table coordinates; before that, it's in the Kinect's own
        /// view. With <paramref name="colorTexture"/>, it's painted with the color camera's picture;
        /// without, it's colored by height once the table is found, and shaded by distance before.
        /// </summary>
        public void Show(DepthFrame frame, TableTracker tracker, Texture2D colorTexture)
        {
            var useColor = colorTexture != null && frame.ColorCoordinates != null;
            meshRenderer.sharedMaterial = useColor ? colorMaterial : heightMaterial;
            if (useColor) colorMaterial.mainTexture = colorTexture;

            // 256 columns looks smooth and is still quick to rebuild every frame.
            var step = Mathf.Max(1, frame.Width / 256);
            var columns = (frame.Width + step - 1) / step;
            var rows = (frame.Height + step - 1) / step;
            var count = columns * rows;
            if (vertices.Length != count)
            {
                vertices = new Vector3[count];
                colors = new Color32[count];
                uvs = new Vector2[count];
                valid = new bool[count];
                depths = new float[count];
                mesh.Clear();
            }

            var surface = tracker.IsCalibrated ? tracker.Surface : null;
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    var i = row * columns + column;
                    var pixel = row * step * frame.Width + column * step;
                    var p = frame.Points[pixel];
                    valid[i] = DepthFrame.IsValid(p);
                    depths[i] = p.Z;
                    if (useColor && valid[i])
                    {
                        var c = frame.ColorCoordinates[pixel];
                        // Parts the color camera can't see, like the very edge of the view, are left out.
                        if (float.IsNaN(c.X) || c.X < 0f || c.X > 1f || c.Y < 0f || c.Y > 1f) valid[i] = false;
                        else uvs[i] = new Vector2(c.X, c.Y);
                    }
                    if (!valid[i])
                    {
                        vertices[i] = Vector3.zero;
                        continue;
                    }
                    if (surface != null)
                    {
                        var t = surface.Frame.ToTable(p);
                        vertices[i] = new Vector3(t.X, t.Y, t.Z);
                        colors[i] = ColorFor(t, surface);
                    }
                    else
                    {
                        // Camera space has X toward the sensor's left; flip it so the view isn't mirrored.
                        vertices[i] = new Vector3(-p.X, p.Y, p.Z);
                        var shade = (byte)Mathf.Clamp(230 - p.Z * 50f, 60, 230);
                        colors[i] = new Color32(shade, shade, (byte)Mathf.Min(255, shade + 20), 255);
                    }
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
            mesh.uv = uvs;
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private static Color32 ColorFor(System.Numerics.Vector3 t, TableSurface surface)
        {
            var onTable = surface.Contains(t.X, t.Z);
            if (onTable && t.Y > HandSteering.MinHandHeight) return HandColor;
            if (onTable && t.Y >= ObstacleMap.MinHeight) return ObjectColor;
            if (onTable && t.Y > -0.02f) return TableColor;
            return SurroundColor;
        }
    }
}

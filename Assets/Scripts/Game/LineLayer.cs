using System.Collections.Generic;
using UnityEngine;

namespace ShapesKinect.Game
{
    /// <summary>
    /// Thin glowing lines lying on the play surface, like the arena's edge or the rings around
    /// objects on it: a soft wide glow with a bright thin core.
    /// </summary>
    public sealed class LineLayer : MonoBehaviour
    {
        private readonly List<float> segments = new List<float>();
        private Mesh mesh;
        private Color color = Color.white;

        /// <summary>Width in meters and opacity of each pass of a line: glow first, then core.</summary>
        private static readonly (float width, float alpha)[] Layers = { (0.016f, 0.3f), (0.005f, 0.95f) };

        /// <summary>Just above the surface, so lines don't flicker into it.</summary>
        private const float Lift = 0.003f;

        private void Awake()
        {
            mesh = new Mesh { name = "Lines" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = Materials.VertexColor();
        }

        public void SetColor(Color lineColor) => color = lineColor;

        public void Clear()
        {
            segments.Clear();
            Rebuild();
        }

        /// <summary>Starts a new set of lines: call <see cref="Add"/> for each, then <see cref="Rebuild"/>.</summary>
        public void Begin() => segments.Clear();

        public void Add(float x1, float z1, float x2, float z2)
        {
            segments.Add(x1);
            segments.Add(z1);
            segments.Add(x2);
            segments.Add(z2);
        }

        /// <summary>Rebuilds the lines, lying at <paramref name="heightAt"/>(x, z), or at height 0 without it.</summary>
        public void Rebuild(System.Func<float, float, float> heightAt = null)
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            foreach (var layer in Layers)
            {
                var layerColor = new Color(color.r, color.g, color.b, color.a * layer.alpha);
                var half = layer.width / 2;
                for (var i = 0; i + 3 < segments.Count; i += 4)
                {
                    var a = new Vector2(segments[i], segments[i + 1]);
                    var b = new Vector2(segments[i + 2], segments[i + 3]);
                    var along = b - a;
                    if (along.sqrMagnitude < 1e-10f) continue;
                    // Stretched past both ends so neighboring segments overlap at the joints.
                    along = along.normalized * half;
                    var side = new Vector2(-along.y, along.x);
                    a -= along;
                    b += along;
                    var start = vertices.Count;
                    foreach (var corner in new[] { a - side, b - side, b + side, a + side })
                    {
                        vertices.Add(new Vector3(corner.x, (heightAt?.Invoke(corner.x, corner.y) ?? 0f) + Lift, corner.y));
                        colors.Add(layerColor);
                    }
                    triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                }
            }
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }
    }
}

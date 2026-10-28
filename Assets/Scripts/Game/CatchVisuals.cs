using System;
using System.Collections.Generic;
using ShapesKinect.Core;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace ShapesKinect.Game
{
    /// <summary>
    /// Draws Shape catcher: the shapes floating around the player, spinning, popping in when they
    /// appear and shrinking away as they fade, the called one pulsing; and a glowing ball on each
    /// hand and foot that can catch. Positions come in the Kinect's camera space and are drawn in
    /// the game's world through <see cref="ToWorld"/>.
    /// </summary>
    public sealed class CatchVisuals : MonoBehaviour
    {
        /// <summary>Converts a camera-space point to the game's world.</summary>
        public Func<NVector3, Vector3> ToWorld;

        private readonly Dictionary<int, Transform> shapes = new Dictionary<int, Transform>();
        private readonly Dictionary<ShapeType, Material> materials = new Dictionary<ShapeType, Material>();
        private readonly List<Transform> markers = new List<Transform>();
        private readonly HashSet<int> seen = new HashSet<int>();
        private Material markerMaterial;

        private static readonly Color MarkerColor = new Color(0.67f, 0.47f, 0.92f, 0.75f);
        private const float MarkerSize = 0.05f;
        /// <summary>How long shapes take to pop in, and to shrink away at the end of their life, in seconds.</summary>
        private const float PopIn = 0.3f;
        private const float FadeOut = 1f;

        private void Awake()
        {
            foreach (var type in ShapeTypes.All) materials[type] = Materials.Lit(ShapeVisuals.ToColor(type.Color()));
            markerMaterial = Materials.Lit(MarkerColor);
        }

        public void Hide()
        {
            foreach (var shape in shapes.Values) Destroy(shape.gameObject);
            shapes.Clear();
            foreach (var marker in markers) marker.gameObject.SetActive(false);
        }

        /// <summary>Draws <paramref name="round"/>, and markers on the hands and feet of <paramref name="bodies"/> the Kinect sees.</summary>
        public void Show(CatchRound round, IReadOnlyList<Body> bodies)
        {
            var time = Time.time;
            var rules = round?.Rules ?? new CatchRules();
            seen.Clear();
            if (round != null)
            {
                foreach (var shape in round.Shapes)
                {
                    seen.Add(shape.Id);
                    if (!shapes.TryGetValue(shape.Id, out var t))
                    {
                        t = Make(shape.Type.Label(), MeshFor(shape.Type), materials[shape.Type]);
                        shapes[shape.Id] = t;
                    }
                    var phase = shape.Id * 1.7f;
                    var grow = Mathf.Clamp01(shape.Age / PopIn);
                    var shrink = Mathf.Clamp01((rules.ShapeLife - shape.Age) / FadeOut);
                    // The shape worth points pulses so it stands out.
                    var pulse = shape.Type == round.Target ? 1f + 0.18f * Mathf.Sin(time * 8f) : 1f;
                    t.position = ToWorld(shape.Position) + Vector3.up * (0.015f * Mathf.Sin(time * 2.5f + phase));
                    t.rotation = Quaternion.Euler(20f * Mathf.Sin(time + phase), time * 60f + phase * 30f, 0f);
                    t.localScale = Vector3.one * (rules.ShapeRadius * SizeFor(shape.Type) * pulse * Mathf.Min(grow, shrink));
                }
            }
            var gone = new List<int>();
            foreach (var id in shapes.Keys) if (!seen.Contains(id)) gone.Add(id);
            foreach (var id in gone)
            {
                Destroy(shapes[id].gameObject);
                shapes.Remove(id);
            }

            var used = 0;
            foreach (var body in bodies)
            {
                foreach (var joint in Body.Catchers)
                {
                    var at = body[joint];
                    if (at == null || !body.Sees(joint)) continue;
                    if (used == markers.Count) markers.Add(Make("Catcher", MeshFactory.Sphere, markerMaterial));
                    var marker = markers[used++];
                    marker.gameObject.SetActive(true);
                    marker.position = ToWorld(at.Value);
                    marker.localScale = Vector3.one * MarkerSize;
                }
            }
            for (var i = used; i < markers.Count; i++) markers[i].gameObject.SetActive(false);
        }

        private static Mesh MeshFor(ShapeType type)
        {
            switch (type)
            {
                case ShapeType.Cube: return MeshFactory.Cube;
                case ShapeType.Triangle: return MeshFactory.Pyramid;
                case ShapeType.Sphere: return MeshFactory.Sphere;
                default: return MeshFactory.Star;
            }
        }

        /// <summary>Scales each shape so they all look about the same size.</summary>
        private static float SizeFor(ShapeType type)
        {
            switch (type)
            {
                case ShapeType.Cube: return 0.75f;
                case ShapeType.Triangle: return 1.1f;
                case ShapeType.Sphere: return 0.9f;
                default: return 0.75f;
            }
        }

        private Transform Make(string objectName, Mesh mesh, Material material)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }
    }
}

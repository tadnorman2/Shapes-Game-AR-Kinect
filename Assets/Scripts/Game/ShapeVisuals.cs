using System.Collections.Generic;
using ShapesKinect.Core;
using UnityEngine;

namespace ShapesKinect.Game
{
    /// <summary>Draws a round: the ball, the shapes spinning and bobbing, and soft shadows under them.</summary>
    public sealed class ShapeVisuals : MonoBehaviour
    {
        private Transform ball;
        private Transform ballShadow;
        private Transform handMarker;
        private readonly Dictionary<int, (Transform shape, Transform shadow)> shapes = new Dictionary<int, (Transform, Transform)>();
        private readonly Dictionary<ShapeType, Material> materials = new Dictionary<ShapeType, Material>();
        private readonly HashSet<int> seen = new HashSet<int>();
        private Material shadowMaterial;

        private static readonly Color BallColor = new Color(0.75f, 0.97f, 1f);
        private static readonly Color HandColor = new Color(0.67f, 0.47f, 0.92f, 0.6f);

        private void Awake()
        {
            shadowMaterial = Materials.VertexColor();
            shadowMaterial.color = new Color(0f, 0f, 0f, 0.45f);
            foreach (var type in ShapeTypes.All) materials[type] = Materials.Lit(ToColor(type.Color()));
            ball = MakeObject("Ball", MeshFactory.Sphere, Materials.Lit(BallColor));
            ballShadow = MakeObject("Ball shadow", MeshFactory.Disc, shadowMaterial);
            var handMaterial = Materials.VertexColor();
            handMaterial.color = HandColor;
            handMarker = MakeObject("Hand", MeshFactory.Disc, handMaterial);
            Hide();
        }

        public static Color ToColor(int rgb) => new Color((rgb >> 16 & 0xFF) / 255f, (rgb >> 8 & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        public void Hide()
        {
            ball.gameObject.SetActive(false);
            ballShadow.gameObject.SetActive(false);
            handMarker.gameObject.SetActive(false);
            foreach (var pair in shapes.Values)
            {
                Destroy(pair.shape.gameObject);
                Destroy(pair.shadow.gameObject);
            }
            shapes.Clear();
        }

        /// <summary>
        /// Draws <paramref name="round"/>, and a marker on the table under the steering hand if
        /// there is one.
        /// </summary>
        public void Show(GameRound round, Vector3? hand)
        {
            var rules = round.Rules;
            var r = rules.PlayerRadius;
            ball.gameObject.SetActive(true);
            ball.localPosition = new Vector3(round.PlayerX, r, round.PlayerZ);
            ball.localScale = Vector3.one * r;
            ballShadow.gameObject.SetActive(true);
            ballShadow.localPosition = new Vector3(round.PlayerX, 0.002f, round.PlayerZ);
            ballShadow.localScale = new Vector3(r * 1.1f, 1f, r * 1.1f);

            handMarker.gameObject.SetActive(hand != null);
            if (hand != null)
            {
                handMarker.localPosition = new Vector3(hand.Value.x, 0.004f, hand.Value.z);
                handMarker.localScale = new Vector3(0.025f, 1f, 0.025f);
            }

            var time = Time.time;
            seen.Clear();
            foreach (var shape in round.Shapes)
            {
                seen.Add(shape.Id);
                if (!shapes.TryGetValue(shape.Id, out var pair))
                {
                    pair = (MakeObject(shape.Type.Label(), MeshFor(shape.Type), materials[shape.Type]),
                        MakeObject("Shadow", MeshFactory.Disc, shadowMaterial));
                    shapes[shape.Id] = pair;
                }
                var phase = shape.Id * 1.7f;
                var bob = 0.006f * Mathf.Sin(time * 3f + phase);
                // The shape worth points pulses so it stands out.
                var pulse = shape.Type == round.Target ? 1f + 0.15f * Mathf.Sin(time * 8f) : 1f;
                var size = rules.ShapeRadius * pulse * SizeFor(shape.Type);
                pair.shape.localPosition = new Vector3(shape.X, rules.ShapeRadius + 0.008f + bob, shape.Z);
                pair.shape.localRotation = Quaternion.Euler(0f, time * 70f + phase * 30f, 0f);
                pair.shape.localScale = Vector3.one * size;
                pair.shadow.localPosition = new Vector3(shape.X, 0.002f, shape.Z);
                pair.shadow.localScale = new Vector3(rules.ShapeRadius, 1f, rules.ShapeRadius);
            }
            var gone = new List<int>();
            foreach (var id in shapes.Keys) if (!seen.Contains(id)) gone.Add(id);
            foreach (var id in gone)
            {
                Destroy(shapes[id].shape.gameObject);
                Destroy(shapes[id].shadow.gameObject);
                shapes.Remove(id);
            }
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

        private Transform MakeObject(string objectName, Mesh mesh, Material material)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go.transform;
        }
    }
}

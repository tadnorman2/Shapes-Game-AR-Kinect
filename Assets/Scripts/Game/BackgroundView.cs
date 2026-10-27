using UnityEngine;

namespace ShapesKinect.Game
{
    /// <summary>
    /// Shows the Kinect's color picture full-screen behind everything, at its own shape, with black
    /// bars if the window is a different shape. A camera of its own draws it first, so the game's
    /// camera can draw the ball and shapes on top.
    /// </summary>
    public sealed class BackgroundView : MonoBehaviour
    {
        /// <summary>A layer only the background uses, so the game's camera can leave it out.</summary>
        public const int Layer = 30;

        private Camera backgroundCamera;
        private Transform quad;
        private Mesh mesh;
        private Material material;
        private bool flipped;

        private void Awake()
        {
            backgroundCamera = gameObject.AddComponent<Camera>();
            backgroundCamera.orthographic = true;
            backgroundCamera.orthographicSize = 0.5f;
            backgroundCamera.clearFlags = CameraClearFlags.SolidColor;
            backgroundCamera.backgroundColor = Color.black;
            backgroundCamera.cullingMask = 1 << Layer;
            backgroundCamera.nearClipPlane = 0.01f;
            backgroundCamera.farClipPlane = 2f;
            backgroundCamera.depth = -100;

            mesh = new Mesh { name = "Background" };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
            });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            SetUvs(false);
            material = Materials.UnlitTexture();

            var go = new GameObject("Background picture") { layer = Layer };
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            quad = go.transform;
            // Far away from the game's world, so nothing else ever overlaps it.
            transform.position = new Vector3(0f, -1000f, 0f);
            Hide();
        }

        /// <summary>
        /// Shows <paramref name="picture"/>, mirrored left to right if <paramref name="flip"/>, and
        /// returns the part of the screen it covers, for the game's camera to draw into.
        /// </summary>
        public Rect Show(Texture2D picture, bool flip)
        {
            backgroundCamera.enabled = true;
            quad.gameObject.SetActive(true);
            material.mainTexture = picture;
            if (flip != flipped) SetUvs(flip);

            var screenAspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            var pictureAspect = (float)picture.width / picture.height;
            Rect area;
            if (screenAspect >= pictureAspect)
            {
                // Bars at the sides.
                quad.localScale = new Vector3(pictureAspect, 1f, 1f);
                var width = pictureAspect / screenAspect;
                area = new Rect((1f - width) / 2f, 0f, width, 1f);
            }
            else
            {
                // Bars at the top and bottom.
                quad.localScale = new Vector3(screenAspect, screenAspect / pictureAspect, 1f);
                var height = screenAspect / pictureAspect;
                area = new Rect(0f, (1f - height) / 2f, 1f, height);
            }
            return area;
        }

        public void Hide()
        {
            backgroundCamera.enabled = false;
            quad.gameObject.SetActive(false);
        }

        /// <summary>
        /// The picture's first row is its top, and textures put their first row at the bottom, so
        /// the top of the quad shows the start of the texture.
        /// </summary>
        private void SetUvs(bool flip)
        {
            flipped = flip;
            float left = flip ? 1f : 0f, right = flip ? 0f : 1f;
            mesh.SetUVs(0, new[]
            {
                new Vector2(left, 1f), new Vector2(right, 1f),
                new Vector2(right, 0f), new Vector2(left, 0f),
            });
        }
    }
}

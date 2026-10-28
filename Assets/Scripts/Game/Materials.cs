using UnityEngine;

namespace ShapesKinect.Game
{
    /// <summary>Materials made in code, working in both Unity's built-in and Universal render pipelines.</summary>
    public static class Materials
    {
        /// <summary>A lit, solid color, for the ball and the shapes.</summary>
        public static Material Lit(Color color)
        {
            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.4f);
            return material;
        }

        /// <summary>Unlit, showing a texture as it is: for the color camera's picture.</summary>
        public static Material UnlitTexture()
        {
            var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Universal Render Pipeline/Unlit");
            return new Material(shader);
        }

        /// <summary>
        /// Invisible, but hides whatever's drawn behind it: for the Kinect's view of the room, so
        /// the game's shapes go behind real people and things. Null if the shader is missing.
        /// </summary>
        public static Material DepthMask()
        {
            var shader = Shader.Find("Shapes/Depth Mask");
            return shader == null ? null : new Material(shader);
        }

        /// <summary>Unlit, see-through, colored per vertex, drawn from both sides: for the depth view, lines and shadows.</summary>
        public static Material VertexColor()
        {
            return new Material(Shader.Find("Sprites/Default"));
        }
    }
}

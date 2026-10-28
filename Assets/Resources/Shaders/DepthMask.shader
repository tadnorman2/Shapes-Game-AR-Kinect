// Draws nothing you can see, but fills in depth: drawn first, it hides whatever the game draws
// behind it. The game draws the Kinect's view of the room with it, so its shapes go behind real
// people and furniture. It sits in Resources so builds include it.
Shader "Shapes/Depth Mask"
{
    SubShader
    {
        Tags { "Queue" = "Geometry-10" "RenderType" = "Opaque" }
        Pass
        {
            ColorMask 0
            ZWrite On
            Cull Off
        }
    }
}

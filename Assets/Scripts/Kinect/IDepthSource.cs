using System;
using System.Collections.Generic;
using ShapesKinect.Core;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace ShapesKinect.Kinect
{
    /// <summary>Somewhere depth images, and hands over the table, come from: a Kinect or the simulator.</summary>
    public interface IDepthSource : IDisposable
    {
        /// <summary>A short name to show the player, like "Kinect v2".</summary>
        string Name { get; }

        /// <summary>
        /// The newest depth image, or null before the first one arrives. Sources reuse a couple of
        /// frames in turn to save memory, so it's only good until the next <see cref="Tick"/>.
        /// </summary>
        DepthFrame Latest { get; }

        /// <summary>The color camera's newest image, or null without one.</summary>
        Texture2D ColorTexture { get; }

        /// <summary>
        /// The color camera, in the depth camera's camera space, once it's known; null without one.
        /// Its image is <see cref="ColorTexture"/> as stored, row 0 at the top.
        /// </summary>
        PinholeCamera ColorCamera { get; }

        /// <summary>Call once per Unity frame to pick up new images.</summary>
        void Tick();

        /// <summary>Adds the camera-space position of each tracked hand to <paramref name="hands"/>.</summary>
        void GetHands(List<NVector3> hands);
    }
}

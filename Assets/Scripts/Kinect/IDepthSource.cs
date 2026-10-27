using System;
using System.Collections.Generic;
using ShapesKinect.Core;
using NVector3 = System.Numerics.Vector3;

namespace ShapesKinect.Kinect
{
    /// <summary>Somewhere depth images, and hands over the table, come from: a Kinect or the simulator.</summary>
    public interface IDepthSource : IDisposable
    {
        /// <summary>A short name to show the player, like "Kinect v2".</summary>
        string Name { get; }

        /// <summary>The newest depth image, or null before the first one arrives.</summary>
        DepthFrame Latest { get; }

        /// <summary>Call once per Unity frame to pick up new images.</summary>
        void Tick();

        /// <summary>Adds the camera-space position of each tracked hand to <paramref name="hands"/>.</summary>
        void GetHands(List<NVector3> hands);
    }
}

using System;
using System.Collections.Generic;
using ShapesKinect.Core;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace ShapesKinect.Kinect
{
    /// <summary>
    /// Plays the part of a Kinect when none is connected, rendering depth of a simulated table.
    /// The number keys put objects on the table and take them off again, and the game can hold a
    /// simulated hand over it, so everything can be tried without the real thing.
    /// </summary>
    public sealed class SimulatedDepthSource : IDepthSource
    {
        public string Name => "Simulated Kinect";
        public DepthFrame Latest { get; private set; }
        public Texture2D ColorTexture => null;
        public PinholeCamera ColorCamera => null;
        public SimulatedTable Room { get; } = new SimulatedTable();

        /// <summary>Objects the number keys toggle, in the simulated room's coordinates.</summary>
        public static readonly (string name, Box box)[] Props =
        {
            ("Cup", Box.OnTable(0.25f, 0.65f, 0.08f, 0.08f, 0.1f)),
            ("Book", Box.OnTable(-0.3f, 0.5f, 0.17f, 0.24f, 0.03f)),
            ("Box", Box.OnTable(0f, 0.85f, 0.2f, 0.12f, 0.12f)),
        };

        private readonly bool[] propOn = new bool[Props.Length];
        private readonly System.Random random = new System.Random();
        private readonly DepthFrame[] frames = { new DepthFrame(256, 212), new DepthFrame(256, 212) };
        private int nextFrame;
        private float nextRender;

        /// <summary>Renders at about the Kinect's rate, at half its resolution to keep it quick.</summary>
        private const float FrameInterval = 1f / 30f;

        public void Tick()
        {
            if (Time.unscaledTime < nextRender) return;
            nextRender = Time.unscaledTime + FrameInterval;
            var frame = frames[nextFrame];
            nextFrame = 1 - nextFrame;
            Room.RenderInto(frame, random);
            Latest = frame;
        }

        public bool IsPropOn(int index) => propOn[index];

        public void ToggleProp(int index)
        {
            propOn[index] = !propOn[index];
            Room.Objects.Clear();
            for (var i = 0; i < Props.Length; i++)
            {
                if (propOn[i]) Room.Objects.Add(Props[i].box);
            }
        }

        /// <summary>Holds the simulated hand at a camera-space point, or takes it away with null.</summary>
        public void SetHand(NVector3? camera) => Room.Hand = camera == null ? (NVector3?)null : Room.CameraToRoom(camera.Value);

        public void GetHands(List<NVector3> hands)
        {
            if (Room.Hand != null) hands.Add(Room.RoomToCamera(Room.Hand.Value));
        }

        public void Dispose()
        {
        }
    }
}

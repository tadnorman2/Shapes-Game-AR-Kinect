using System;
using System.Collections.Generic;
using ShapesKinect.Core;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace ShapesKinect.Kinect
{
    /// <summary>
    /// Plays the part of a Kinect when none is connected, rendering depth of a simulated table,
    /// and a grey color picture of it with a card on it. The number keys put objects and cards
    /// on the table and take them off again, and the game can hold a simulated hand over it, so
    /// everything can be tried without the real thing.
    /// </summary>
    public sealed class SimulatedDepthSource : IDepthSource
    {
        public string Name => "Simulated Kinect";
        public DepthFrame Latest { get; private set; }
        public Texture2D ColorTexture { get; private set; }
        public PinholeCamera ColorCamera { get; } = SimulatedTable.ColorCamera();
        public GrayImage ColorGray { get; } = new GrayImage(960, 540, 2);
        public SimulatedTable Room { get; } = new SimulatedTable();

        /// <summary>Objects the number keys toggle, in the simulated room's coordinates.</summary>
        public static readonly (string name, Box box)[] Props =
        {
            ("Cup", Box.OnTable(0.25f, 0.65f, 0.08f, 0.08f, 0.1f)),
            ("Book", Box.OnTable(-0.3f, 0.5f, 0.17f, 0.24f, 0.03f)),
            ("Box", Box.OnTable(0f, 0.85f, 0.2f, 0.12f, 0.12f)),
        };

        /// <summary>Cards the number keys after the props put down, in the simulated room: which card, where, and turned how far.</summary>
        public static readonly (string name, (int id, float x, float z, float turn) card)[] Cards =
        {
            ("Card 1", (0, 0f, 0.6f, 0.2f)),
            ("Card 2", (1, 0.05f, 0.62f, -0.3f)),
            ("Card 3", (2, -0.05f, 0.58f, 0.6f)),
        };

        private readonly bool[] propOn = new bool[Props.Length];
        private int cardOn = -1;
        private bool colorStale = true;

        /// <summary>Starts with the first card on the table.</summary>
        public SimulatedDepthSource() => ToggleCard(0);
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
            // A new picture each time, as a camera gives, even if it's the same as the last.
            if (colorStale) RenderColor();
            else ColorGray.Number++;
        }

        /// <summary>
        /// Renders the color picture afresh, which is slow, so only when what's on the table
        /// changes; the simulated hand isn't in it.
        /// </summary>
        private void RenderColor()
        {
            colorStale = false;
            var hand = Room.Hand;
            Room.Hand = null;
            Room.RenderColor(ColorCamera, ColorGray, random);
            Room.Hand = hand;
            if (ColorTexture == null)
            {
                ColorTexture = new Texture2D(ColorGray.Width, ColorGray.Height, TextureFormat.RGBA32, false) { name = "Simulated color" };
            }
            var pixels = new Color32[ColorGray.Pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                var g = ColorGray.Pixels[i];
                pixels[i] = new Color32(g, g, g, 255);
            }
            // Row 0 of the picture is its top, which textures keep at the end.
            var flipped = new Color32[pixels.Length];
            for (var y = 0; y < ColorGray.Height; y++) Array.Copy(pixels, y * ColorGray.Width, flipped, (ColorGray.Height - 1 - y) * ColorGray.Width, ColorGray.Width);
            ColorTexture.SetPixels32(flipped);
            ColorTexture.Apply(false);
        }

        /// <summary>Which card is on the simulated table, as an index into <see cref="Cards"/>, or -1.</summary>
        public int CardOn => cardOn;

        /// <summary>Puts card <paramref name="index"/> of <see cref="Cards"/> on the table in place of any other, or takes it off if it's there.</summary>
        public void ToggleCard(int index)
        {
            cardOn = cardOn == index ? -1 : index;
            Room.Card = cardOn < 0 ? ((int, float, float, float)?)null : Cards[cardOn].card;
            colorStale = true;
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
            colorStale = true;
        }

        /// <summary>Holds the simulated hand at a camera-space point, or takes it away with null.</summary>
        public void SetHand(NVector3? camera) => Room.Hand = camera == null ? (NVector3?)null : Room.CameraToRoom(camera.Value);

        /// <summary>
        /// For Shape catcher: a person standing 2 m in front of the simulated Kinect, their right
        /// hand where <see cref="SetCatcherHand"/> puts it, or down by their side.
        /// </summary>
        public void GetBodies(List<Body> bodies)
        {
            bodies.Clear();
            var person = new Body();
            var middle = new NVector3(0f, 0f, 2f);
            person.Set(BodyJoint.Head, middle + new NVector3(0f, 0.45f, 0f), true);
            person.Set(BodyJoint.SpineShoulder, middle + new NVector3(0f, 0.3f, 0f), true);
            person.Set(BodyJoint.SpineMid, middle, true);
            person.Set(BodyJoint.SpineBase, middle + new NVector3(0f, -0.25f, 0f), true);
            // Camera space has X to the Kinect's left, which is the person's right as they face it.
            person.Set(BodyJoint.HandLeft, middle + new NVector3(-0.25f, -0.3f, -0.05f), true);
            person.Set(BodyJoint.HandRight, catcherHand ?? middle + new NVector3(0.25f, -0.3f, -0.05f), true);
            person.Set(BodyJoint.FootLeft, middle + new NVector3(-0.15f, -1f, 0f), true);
            person.Set(BodyJoint.FootRight, middle + new NVector3(0.15f, -1f, 0f), true);
            bodies.Add(person);
        }

        private NVector3? catcherHand;

        /// <summary>Puts the simulated person's right hand at a camera-space point, or back by their side with null.</summary>
        public void SetCatcherHand(NVector3? camera) => catcherHand = camera;

        public void GetHands(List<NVector3> hands)
        {
            if (Room.Hand != null) hands.Add(Room.RoomToCamera(Room.Hand.Value));
        }

        public void Dispose()
        {
            if (ColorTexture != null) UnityEngine.Object.Destroy(ColorTexture);
        }
    }
}

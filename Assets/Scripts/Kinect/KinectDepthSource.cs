// Only compiled once Microsoft's Kinect v2 Unity plugin is in the project; the editor script in
// Assets/Editor/KinectSetup.cs turns KINECT_V2 on when it finds the plugin. See the README.
#if KINECT_V2
using System.Collections.Generic;
using ShapesKinect.Core;
using UnityEngine;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace ShapesKinect.Kinect
{
    /// <summary>
    /// Depth, color and hands from a real Kinect v2, through Microsoft's Unity plugin. Buffers are
    /// allocated once and reused, since a new 512 x 424 frame every 1/30 s is a lot of garbage.
    /// </summary>
    public sealed class KinectDepthSource : IDepthSource
    {
        public string Name => "Kinect v2";
        public DepthFrame Latest { get; private set; }
        public Texture2D ColorTexture { get; private set; }

        /// <summary>Whether a Kinect is plugged in and powered, as far as the runtime knows.</summary>
        public bool IsAvailable => sensor != null && sensor.IsAvailable;

        private readonly Windows.Kinect.KinectSensor sensor;
        private readonly Windows.Kinect.DepthFrameReader depthReader;
        private readonly Windows.Kinect.ColorFrameReader colorReader;
        private readonly Windows.Kinect.BodyFrameReader bodyReader;
        private readonly ushort[] depthData;
        private readonly Windows.Kinect.CameraSpacePoint[] cameraPoints;
        private readonly Windows.Kinect.ColorSpacePoint[] colorPoints;
        private readonly byte[] colorData;
        private readonly int colorWidth;
        private readonly int colorHeight;
        private readonly Windows.Kinect.Body[] bodies;
        private readonly DepthFrame[] frames;
        private int nextFrame;
        private long frameNumber;

        private KinectDepthSource(Windows.Kinect.KinectSensor sensor)
        {
            this.sensor = sensor;
            var depthDescription = sensor.DepthFrameSource.FrameDescription;
            var width = depthDescription.Width;
            var height = depthDescription.Height;
            depthData = new ushort[width * height];
            cameraPoints = new Windows.Kinect.CameraSpacePoint[width * height];
            colorPoints = new Windows.Kinect.ColorSpacePoint[width * height];
            frames = new DepthFrame[2];
            for (var i = 0; i < frames.Length; i++)
            {
                frames[i] = new DepthFrame(width, height) { ColorCoordinates = new NVector2[width * height] };
            }

            var colorDescription = sensor.ColorFrameSource.CreateFrameDescription(Windows.Kinect.ColorImageFormat.Rgba);
            colorWidth = colorDescription.Width;
            colorHeight = colorDescription.Height;
            colorData = new byte[colorDescription.BytesPerPixel * colorDescription.LengthInPixels];

            depthReader = sensor.DepthFrameSource.OpenReader();
            colorReader = sensor.ColorFrameSource.OpenReader();
            bodyReader = sensor.BodyFrameSource.OpenReader();
            bodies = new Windows.Kinect.Body[sensor.BodyFrameSource.BodyCount];
        }

        /// <summary>Opens the Kinect, or returns null if the Kinect runtime isn't there.</summary>
        public static KinectDepthSource TryOpen()
        {
            var sensor = Windows.Kinect.KinectSensor.GetDefault();
            if (sensor == null) return null;
            if (!sensor.IsOpen) sensor.Open();
            return new KinectDepthSource(sensor);
        }

        public void Tick()
        {
            using (var frame = colorReader.AcquireLatestFrame())
            {
                if (frame != null)
                {
                    if (ColorTexture == null)
                    {
                        ColorTexture = new Texture2D(colorWidth, colorHeight, TextureFormat.RGBA32, false)
                        {
                            name = "Kinect color",
                            wrapMode = TextureWrapMode.Clamp,
                        };
                    }
                    frame.CopyConvertedFrameDataToArray(colorData, Windows.Kinect.ColorImageFormat.Rgba);
                    ColorTexture.LoadRawTextureData(colorData);
                    ColorTexture.Apply(false);
                }
            }

            using (var frame = depthReader.AcquireLatestFrame())
            {
                if (frame != null)
                {
                    frame.CopyFrameDataToArray(depthData);
                    sensor.CoordinateMapper.MapDepthFrameToCameraSpace(depthData, cameraPoints);
                    sensor.CoordinateMapper.MapDepthFrameToColorSpace(depthData, colorPoints);
                    var result = frames[nextFrame];
                    nextFrame = 1 - nextFrame;
                    result.Number = ++frameNumber;
                    var colors = result.ColorCoordinates;
                    for (var i = 0; i < cameraPoints.Length; i++)
                    {
                        var p = cameraPoints[i];
                        // The mapper marks pixels with no reading as negative infinity.
                        result.Points[i] = float.IsInfinity(p.Z) || float.IsNaN(p.Z) || p.Z <= 0f
                            ? DepthFrame.Invalid
                            : new NVector3(p.X, p.Y, p.Z);
                        var c = colorPoints[i];
                        colors[i] = float.IsInfinity(c.X) || float.IsInfinity(c.Y)
                            ? new NVector2(float.NaN, float.NaN)
                            : new NVector2(c.X / colorWidth, c.Y / colorHeight);
                    }
                    Latest = result;
                }
            }

            using (var frame = bodyReader.AcquireLatestFrame())
            {
                if (frame != null) frame.GetAndRefreshBodyData(bodies);
            }
        }

        public void GetHands(List<NVector3> hands)
        {
            foreach (var body in bodies)
            {
                if (body == null || !body.IsTracked) continue;
                foreach (var type in new[] { Windows.Kinect.JointType.HandLeft, Windows.Kinect.JointType.HandRight })
                {
                    var joint = body.Joints[type];
                    if (joint.TrackingState != Windows.Kinect.TrackingState.Tracked) continue;
                    hands.Add(new NVector3(joint.Position.X, joint.Position.Y, joint.Position.Z));
                }
            }
        }

        public void Dispose()
        {
            depthReader?.Dispose();
            colorReader?.Dispose();
            bodyReader?.Dispose();
            if (sensor != null && sensor.IsOpen) sensor.Close();
            if (ColorTexture != null) Object.Destroy(ColorTexture);
        }
    }
}
#endif

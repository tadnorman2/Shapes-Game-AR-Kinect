// Only compiled once Microsoft's Kinect v2 Unity plugin is in the project; the editor script in
// Assets/Editor/KinectSetup.cs turns KINECT_V2 on when it finds the plugin. See the README.
#if KINECT_V2
using System.Collections.Generic;
using ShapesKinect.Core;
using ShapesKinect.Game;
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
        public PinholeCamera ColorCamera { get; private set; }

        /// <summary>Whether a Kinect is plugged in and powered, as far as the runtime knows.</summary>
        public bool IsAvailable => sensor != null && sensor.IsAvailable;

        private readonly Windows.Kinect.KinectSensor sensor;
        private readonly Windows.Kinect.DepthFrameReader depthReader;
        private readonly Windows.Kinect.ColorFrameReader colorReader;
        private readonly Windows.Kinect.BodyFrameReader bodyReader;
        private readonly ushort[] depthData;
        private readonly Windows.Kinect.CameraSpacePoint[] cameraPoints;
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
            frames = new DepthFrame[2];
            for (var i = 0; i < frames.Length; i++)
            {
                frames[i] = new DepthFrame(width, height);
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
            Watchdog.Mark("Kinect: working out the color camera");
            if (ColorCamera == null) TryFindColorCamera();

            Watchdog.Mark("Kinect: reading the color picture");
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

            Watchdog.Mark("Kinect: reading depth");
            using (var frame = depthReader.AcquireLatestFrame())
            {
                if (frame != null)
                {
                    frame.CopyFrameDataToArray(depthData);
                    sensor.CoordinateMapper.MapDepthFrameToCameraSpace(depthData, cameraPoints);
                    var result = frames[nextFrame];
                    nextFrame = 1 - nextFrame;
                    result.Number = ++frameNumber;
                    for (var i = 0; i < cameraPoints.Length; i++)
                    {
                        var p = cameraPoints[i];
                        // The mapper marks pixels with no reading as negative infinity.
                        result.Points[i] = float.IsInfinity(p.Z) || float.IsNaN(p.Z) || p.Z <= 0f
                            ? DepthFrame.Invalid
                            : new NVector3(p.X, p.Y, p.Z);
                    }
                    Latest = result;
                }
            }

            Watchdog.Mark("Kinect: reading bodies");
            using (var frame = bodyReader.AcquireLatestFrame())
            {
                if (frame != null) frame.GetAndRefreshBodyData(bodies);
            }
            Watchdog.Mark("Kinect: done");
        }

        /// <summary>
        /// Works out where the color camera is and how its lens maps the world, from the Kinect's
        /// own factory calibration: asks it where a 3D grid of points lands in the color picture,
        /// then fits a camera to the answers. Waits until the Kinect is connected, since the
        /// calibration comes from the device.
        /// </summary>
        private void TryFindColorCamera()
        {
            if (!sensor.IsAvailable) return;
            var grid = new List<Windows.Kinect.CameraSpacePoint>();
            foreach (var depth in new[] { 0.6f, 0.9f, 1.3f, 1.8f, 2.5f, 3.4f })
            {
                for (var i = -4; i <= 4; i++)
                {
                    for (var j = -3; j <= 3; j++)
                    {
                        grid.Add(new Windows.Kinect.CameraSpacePoint { X = i * 0.12f * depth, Y = j * 0.1f * depth, Z = depth });
                    }
                }
            }
            var cameraPoints = grid.ToArray();
            var mapped = new Windows.Kinect.ColorSpacePoint[cameraPoints.Length];
            sensor.CoordinateMapper.MapCameraPointsToColorSpace(cameraPoints, mapped);
            var points = new List<NVector3>();
            var pixels = new List<NVector2>();
            for (var i = 0; i < mapped.Length; i++)
            {
                var c = mapped[i];
                if (float.IsInfinity(c.X) || float.IsNaN(c.X) || c.X < 0 || c.X > colorWidth || c.Y < 0 || c.Y > colorHeight) continue;
                points.Add(new NVector3(cameraPoints[i].X, cameraPoints[i].Y, cameraPoints[i].Z));
                pixels.Add(new NVector2(c.X, c.Y));
            }
            if (points.Count < 50) return;
            ColorCamera = PinholeCamera.Fit(points, pixels, colorWidth, colorHeight);
            if (ColorCamera != null)
            {
                Debug.Log($"[Shapes] Color camera found from {points.Count} points: position {ColorCamera.Position}, " +
                    $"focal {ColorCamera.Fx:0}/{ColorCamera.Fy:0} px, center {ColorCamera.Cx:0},{ColorCamera.Cy:0}, " +
                    $"view {ColorCamera.VerticalFieldOfView:0.0} degrees tall");
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

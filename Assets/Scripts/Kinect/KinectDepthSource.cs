// Only compiled once Microsoft's Kinect v2 Unity plugin is in the project; the editor script in
// Assets/Editor/KinectSetup.cs turns KINECT_V2 on when it finds the plugin. See the README.
#if KINECT_V2
using System.Collections.Generic;
using ShapesKinect.Core;
using NVector3 = System.Numerics.Vector3;

namespace ShapesKinect.Kinect
{
    /// <summary>Depth and hands from a real Kinect v2, through Microsoft's Unity plugin.</summary>
    public sealed class KinectDepthSource : IDepthSource
    {
        public string Name => "Kinect v2";
        public DepthFrame Latest { get; private set; }

        private readonly Windows.Kinect.KinectSensor sensor;
        private readonly Windows.Kinect.DepthFrameReader depthReader;
        private readonly Windows.Kinect.BodyFrameReader bodyReader;
        private readonly ushort[] depthData;
        private readonly Windows.Kinect.CameraSpacePoint[] cameraPoints;
        private readonly Windows.Kinect.Body[] bodies;
        private readonly int width;
        private readonly int height;
        private long frameNumber;

        private KinectDepthSource(Windows.Kinect.KinectSensor sensor)
        {
            this.sensor = sensor;
            var description = sensor.DepthFrameSource.FrameDescription;
            width = description.Width;
            height = description.Height;
            depthData = new ushort[width * height];
            cameraPoints = new Windows.Kinect.CameraSpacePoint[width * height];
            depthReader = sensor.DepthFrameSource.OpenReader();
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
            using (var frame = depthReader.AcquireLatestFrame())
            {
                if (frame != null)
                {
                    frame.CopyFrameDataToArray(depthData);
                    sensor.CoordinateMapper.MapDepthFrameToCameraSpace(depthData, cameraPoints);
                    var result = new DepthFrame(width, height) { Number = ++frameNumber };
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
            bodyReader?.Dispose();
            if (sensor != null && sensor.IsOpen) sensor.Close();
        }
    }
}
#endif

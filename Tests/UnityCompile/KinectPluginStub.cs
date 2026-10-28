// A stand-in for the parts of Microsoft's Kinect v2 Unity plugin the game uses, so CI can compile
// KinectDepthSource. Signatures follow the plugin and Microsoft's Unity samples; nothing here runs.
using System;
using System.Collections.Generic;

namespace Windows.Kinect
{
    public enum ColorImageFormat { None, Rgba, Yuv, Bgra, Bayer, Yuy2 }
    public enum JointType { SpineBase = 0, SpineMid = 1, Head = 3, HandLeft = 7, HandRight = 11, FootLeft = 15, FootRight = 19, SpineShoulder = 20 }
    public enum TrackingState { NotTracked, Inferred, Tracked }

    public struct Vector4
    {
        public float X;
        public float Y;
        public float Z;
        public float W;
    }

    public struct CameraSpacePoint { public float X; public float Y; public float Z; }
    public struct ColorSpacePoint { public float X; public float Y; }

    public struct Joint
    {
        public JointType JointType;
        public CameraSpacePoint Position;
        public TrackingState TrackingState;
    }

    public sealed class FrameDescription
    {
        public int Width => 0;
        public int Height => 0;
        public uint BytesPerPixel => 0;
        public uint LengthInPixels => 0;
    }

    public sealed class Body
    {
        public bool IsTracked => false;
        public Dictionary<JointType, Joint> Joints => null;
    }

    public sealed class DepthFrame : IDisposable
    {
        public void CopyFrameDataToArray(ushort[] frameData) { }
        public void Dispose() { }
    }

    public sealed class ColorFrame : IDisposable
    {
        public void CopyConvertedFrameDataToArray(byte[] frameData, ColorImageFormat colorFormat) { }
        public void Dispose() { }
    }

    public sealed class BodyFrame : IDisposable
    {
        public Vector4 FloorClipPlane => default;
        public void GetAndRefreshBodyData(Body[] bodies) { }
        public void Dispose() { }
    }

    public sealed class DepthFrameReader : IDisposable
    {
        public DepthFrame AcquireLatestFrame() => null;
        public void Dispose() { }
    }

    public sealed class ColorFrameReader : IDisposable
    {
        public ColorFrame AcquireLatestFrame() => null;
        public void Dispose() { }
    }

    public sealed class BodyFrameReader : IDisposable
    {
        public BodyFrame AcquireLatestFrame() => null;
        public void Dispose() { }
    }

    public sealed class DepthFrameSource
    {
        public FrameDescription FrameDescription => null;
        public DepthFrameReader OpenReader() => null;
    }

    public sealed class ColorFrameSource
    {
        public FrameDescription CreateFrameDescription(ColorImageFormat format) => null;
        public ColorFrameReader OpenReader() => null;
    }

    public sealed class BodyFrameSource
    {
        public int BodyCount => 6;
        public BodyFrameReader OpenReader() => null;
    }

    public sealed class CoordinateMapper
    {
        public void MapDepthFrameToCameraSpace(ushort[] depthFrameData, CameraSpacePoint[] cameraSpacePoints) { }
        public void MapDepthFrameToColorSpace(ushort[] depthFrameData, ColorSpacePoint[] colorSpacePoints) { }
        public void MapCameraPointsToColorSpace(CameraSpacePoint[] cameraPoints, ColorSpacePoint[] colorPoints) { }
    }

    public sealed class KinectSensor
    {
        public static KinectSensor GetDefault() => null;
        public bool IsOpen => false;
        public bool IsAvailable => false;
        public void Open() { }
        public void Close() { }
        public DepthFrameSource DepthFrameSource => null;
        public ColorFrameSource ColorFrameSource => null;
        public BodyFrameSource BodyFrameSource => null;
        public CoordinateMapper CoordinateMapper => null;
    }
}

using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>The parts of a tracked person the game uses.</summary>
    public enum BodyJoint { Head, SpineShoulder, SpineMid, SpineBase, HandLeft, HandRight, FootLeft, FootRight }

    /// <summary>
    /// A person the Kinect tracks: where their joints are, in its camera space, and which it can
    /// actually see rather than guess.
    /// </summary>
    public sealed class Body
    {
        public static readonly int JointCount = System.Enum.GetValues(typeof(BodyJoint)).Length;

        private readonly Vector3?[] joints = new Vector3?[JointCount];
        private readonly bool[] seen = new bool[JointCount];

        /// <summary>The joint's position, or null if the Kinect has no idea where it is.</summary>
        public Vector3? this[BodyJoint joint] => joints[(int)joint];

        /// <summary>Whether the Kinect sees the joint, rather than guessing from the rest of the body.</summary>
        public bool Sees(BodyJoint joint) => seen[(int)joint];

        public void Set(BodyJoint joint, Vector3 position, bool isSeen)
        {
            joints[(int)joint] = position;
            seen[(int)joint] = isSeen;
        }

        /// <summary>The body's middle: the middle of its spine, or failing that its base or top.</summary>
        public Vector3? Middle => this[BodyJoint.SpineMid] ?? this[BodyJoint.SpineBase] ?? this[BodyJoint.SpineShoulder];

        /// <summary>The joints that can catch shapes: hands and feet, where the Kinect sees them.</summary>
        public static readonly BodyJoint[] Catchers = { BodyJoint.HandLeft, BodyJoint.HandRight, BodyJoint.FootLeft, BodyJoint.FootRight };
    }
}

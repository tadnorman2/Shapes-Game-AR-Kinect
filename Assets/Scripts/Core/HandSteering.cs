using System;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>
    /// Steers the ball toward the player's hand held over the table: full speed when the hand is
    /// well away from the ball, slowing as the ball catches up, and stopping right under it.
    /// </summary>
    public static class HandSteering
    {
        /// <summary>The hand must be between these heights above the table to steer, in meters.</summary>
        public const float MinHandHeight = ObstacleMap.MaxHeight;
        public const float MaxHandHeight = 0.6f;
        /// <summary>The ball stops when it's this close to under the hand.</summary>
        public const float DeadZone = 0.02f;
        /// <summary>Beyond this far from under the hand, the ball moves at full speed.</summary>
        public const float FullSpeedDistance = 0.12f;

        /// <summary>Whether a hand at <paramref name="hand"/> (table coordinates) is in position to steer.</summary>
        public static bool IsSteering(Vector3 hand) => hand.Y >= MinHandHeight && hand.Y <= MaxHandHeight;

        /// <summary>The move for a ball at (<paramref name="ballX"/>, <paramref name="ballZ"/>) toward the hand, or zero.</summary>
        public static Vector2 Steer(float ballX, float ballZ, Vector3 hand)
        {
            if (!IsSteering(hand)) return Vector2.Zero;
            var toHand = new Vector2(hand.X - ballX, hand.Z - ballZ);
            var distance = toHand.Length();
            if (distance <= DeadZone) return Vector2.Zero;
            var strength = Math.Min((distance - DeadZone) / (FullSpeedDistance - DeadZone), 1f);
            return toHand / distance * strength;
        }
    }
}

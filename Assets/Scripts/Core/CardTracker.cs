using System;
using System.Collections.Generic;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>
    /// Follows the cards on the table from picture to picture: finds them (looking first where
    /// the last one was, which is quicker), works out where the biggest lies, holds it steady
    /// while it lies still, and keeps the last placement for a moment when the card can't be
    /// seen, as when a hand passes over it.
    /// </summary>
    public sealed class CardTracker
    {
        /// <summary>The card being followed, or null if none has been seen lately.</summary>
        public CardPlacement Current { get; private set; }
        /// <summary>Whether the card was seen in the latest picture, rather than held from before.</summary>
        public bool IsVisible { get; private set; }
        /// <summary>The latest sighting of it, for drawing where it was found.</summary>
        public CardSighting LastSighting { get; private set; }

        private readonly CardDetector detector = new CardDetector();
        private GrayImage lastImage;
        private long lastPicture = -1;
        private float lastSeen = float.NegativeInfinity;
        private int picturesSinceSearch = SearchEvery;

        /// <summary>How long to keep a card that can't be seen, in seconds.</summary>
        public const float HoldSeconds = 1.5f;
        /// <summary>How far a card lying still seems to wobble from picture to picture, in meters and degrees.</summary>
        private const float StillWobble = 0.003f;
        private const float StillWobbleDegrees = 1f;
        /// <summary>Moves bigger than these, like picking the card up and putting it elsewhere, are taken at once.</summary>
        private const float SnapDistance = 0.08f;
        private const float SnapDegrees = 30f;
        /// <summary>How much of a new placement goes into the smoothed one: little for a wobble, most for a real move.</summary>
        private const float MinBlend = 0.06f;
        private const float MaxBlend = 0.8f;
        /// <summary>The whole picture is searched at most every this many pictures, as it takes a while.</summary>
        private const int SearchEvery = 3;

        /// <summary>
        /// Takes in the newest pictures; <paramref name="now"/> is the time in seconds. Returns
        /// whether a new grey picture was looked at.
        /// </summary>
        public bool Update(GrayImage image, PinholeCamera camera, DepthFrame depth, float now)
        {
            if (Current != null && now - lastSeen > HoldSeconds)
            {
                Current = null;
                LastSighting = null;
            }
            if (image == null || camera == null || (image == lastImage && image.Number == lastPicture)) return false;
            lastImage = image;
            lastPicture = image.Number;

            // Near where the card was is quick to search. The whole picture takes longer: it's
            // searched when the card has just gone from there, and otherwise every few pictures.
            var sightings = LastSighting != null ? detector.Detect(image, Around(LastSighting, image)) : new List<CardSighting>();
            if (sightings.Count == 0 && (LastSighting != null || ++picturesSinceSearch >= SearchEvery))
            {
                picturesSinceSearch = 0;
                sightings = detector.Detect(image);
            }
            IsVisible = false;
            CardSighting best = null;
            foreach (var s in sightings)
            {
                if (best == null || s.Area > best.Area) best = s;
            }
            if (best == null) return true;
            var placement = CardLocator.Locate(best, camera, depth);
            if (placement == null) return true;
            Current = Smooth(Current, placement);
            LastSighting = best;
            IsVisible = true;
            lastSeen = now;
            return true;
        }

        /// <summary>Forgets the card, e.g. after switching to a different Kinect.</summary>
        public void Reset()
        {
            Current = null;
            LastSighting = null;
            IsVisible = false;
            lastImage = null;
            lastPicture = -1;
        }

        /// <summary>The part of the grey picture around where the card was, with room for it to have moved.</summary>
        private static (int, int, int, int) Around(CardSighting sighting, GrayImage image)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var c in sighting.Corners)
            {
                minX = Math.Min(minX, c.X); maxX = Math.Max(maxX, c.X);
                minY = Math.Min(minY, c.Y); maxY = Math.Max(maxY, c.Y);
            }
            var margin = Math.Max(maxX - minX, maxY - minY) * 0.75f;
            return ((int)((minX - margin) / image.Scale), (int)((minY - margin) / image.Scale),
                (int)((maxX + margin) / image.Scale) + 1, (int)((maxY + margin) / image.Scale) + 1);
        }

        /// <summary>
        /// Blends a new placement into the one so far, by how far it is from it: a card lying
        /// still only ever seems to wobble by a millimeter or a degree or so, and that's averaged
        /// away over many pictures; a real move goes beyond that and is followed straight away.
        /// </summary>
        private static CardPlacement Smooth(CardPlacement before, CardPlacement now)
        {
            if (before == null || before.Id != now.Id) return now;
            var (meters, degrees) = Board.Difference(before.Frame, now.Frame);
            if (meters > SnapDistance || degrees > SnapDegrees) return now;
            // How many wobbles' worth it moved: 1 is a typical wobble of a card lying still.
            var moved = Math.Max(meters / StillWobble, degrees / StillWobbleDegrees);
            var blend = Math.Max(MinBlend, Math.Min(MaxBlend, MinBlend + 0.1f * moved * moved));
            var a = before.Frame;
            var b = now.Frame;
            var origin = Vector3.Lerp(a.Origin, b.Origin, blend);
            var up = Vector3.Normalize(Vector3.Lerp(a.Up, b.Up, blend));
            var forward = Vector3.Lerp(a.Forward, b.Forward, blend);
            var size = before.Size + (now.Size - before.Size) * blend;
            return new CardPlacement(now.Id, new TableFrame(origin, up, forward), size, now.FromDepth);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Numerics;

namespace ShapesKinect.Core
{
    /// <summary>Tunable rules for Shape catcher. Distances are meters, times are seconds.</summary>
    public sealed class CatchRules
    {
        public float RoundSeconds = 120f;
        /// <summary>A shape's size, and how close a hand or foot has to come to its middle to catch it.</summary>
        public float ShapeRadius = 0.07f;
        public float CatchRadius = 0.13f;
        public float SpawnInterval = 0.9f;
        public int MaxShapes = 6;
        /// <summary>How long a shape stays before fading away uncaught.</summary>
        public float ShapeLife = 7f;
        /// <summary>
        /// Where shapes appear around the player, from the middle of their body: this far to
        /// either side, from this far below to this far above, and this far in front of them.
        /// </summary>
        public float ReachSide = 0.75f;
        public float ReachBelow = 0.55f;
        public float ReachAbove = 0.65f;
        public float InFrontNear = 0.1f;
        public float InFrontFar = 0.35f;
        /// <summary>Shapes don't appear this close to a hand or foot, or to each other.</summary>
        public float SpawnClearance = 0.25f;
        /// <summary>How far shapes drift about while they wait, in meters a second.</summary>
        public float Drift = 0.04f;
        public float FreeSeconds = 10f;
        public float TargetSeconds = 15f;
        public int Points = 10;
        public int Penalty = 5;
        public float TargetSpawnChance = 0.4f;
    }

    /// <summary>A shape floating in the air near the player, in the Kinect's camera space.</summary>
    public sealed class FloatingShape
    {
        public int Id { get; }
        public ShapeType Type { get; }
        public Vector3 Position { get; internal set; }
        internal Vector3 Velocity;
        /// <summary>How long it's been there, in seconds.</summary>
        public float Age { get; internal set; }

        public FloatingShape(int id, ShapeType type, Vector3 position, Vector3 velocity)
        {
            Id = id;
            Type = type;
            Position = position;
            Velocity = velocity;
        }
    }

    /// <summary>
    /// One timed round of Shape catcher: shapes appear in the air around the player, within
    /// reach, and they catch them by touching them with a hand or foot. For the first stretch any
    /// shape scores; after that the game calls out one shape at a time, and only that one scores,
    /// while any other costs points. Shapes left too long fade away. Everyone the Kinect tracks
    /// can catch; shapes appear around whoever's nearest the middle of its view. The clock stops
    /// while nobody's in view.
    /// </summary>
    public sealed class CatchRound
    {
        public CatchRules Rules { get; }
        public float Elapsed { get; private set; }
        public int Score { get; private set; }
        public ShapeType? Target { get; private set; }
        public IReadOnlyList<FloatingShape> Shapes => shapes;
        /// <summary>Whether someone's in view to play; while not, the round waits.</summary>
        public bool HasPlayer { get; private set; }
        public float TimeLeft => Math.Max(0f, Rules.RoundSeconds - Elapsed);
        public bool IsFinished => Elapsed >= Rules.RoundSeconds;

        private readonly Random random;
        private readonly List<FloatingShape> shapes = new List<FloatingShape>();
        private int nextId;
        private float spawnTimer;
        private float nextTargetAt;

        public CatchRound(CatchRules rules = null, Random random = null)
        {
            Rules = rules ?? new CatchRules();
            this.random = random ?? new Random();
            nextTargetAt = Rules.FreeSeconds;
        }

        /// <summary>Advances the round by <paramref name="dt"/> seconds, with the people the Kinect tracks now.</summary>
        public List<RoundEvent> Update(float dt, IReadOnlyList<Body> bodies)
        {
            var events = new List<RoundEvent>();
            if (IsFinished) return events;
            var player = Player(bodies);
            HasPlayer = player != null;
            if (!HasPlayer) return events;
            Elapsed += dt;

            foreach (var shape in shapes)
            {
                shape.Age += dt;
                shape.Position += shape.Velocity * dt;
            }
            shapes.RemoveAll(s => s.Age > Rules.ShapeLife);

            // Caught: any hand or foot the Kinect sees close enough.
            for (var i = shapes.Count - 1; i >= 0; i--)
            {
                if (!Touched(shapes[i].Position, bodies)) continue;
                var type = shapes[i].Type;
                shapes.RemoveAt(i);
                var points = PointsFor(type);
                Score = Math.Max(Score + points, 0);
                events.Add(new RoundEvent.Collected(type, points));
            }

            if (IsFinished)
            {
                events.Add(new RoundEvent.Finished(Score));
                return events;
            }

            if (Elapsed >= nextTargetAt)
            {
                var choices = new List<ShapeType>();
                foreach (var type in ShapeTypes.All) if (type != Target) choices.Add(type);
                Target = choices[random.Next(choices.Count)];
                nextTargetAt += Rules.TargetSeconds;
                events.Add(new RoundEvent.TargetChanged(Target.Value));
                // At least one of it out straight away.
                if (!shapes.Exists(s => s.Type == Target)) Spawn(Target.Value, player, bodies);
            }

            spawnTimer += dt;
            while (spawnTimer >= Rules.SpawnInterval)
            {
                spawnTimer -= Rules.SpawnInterval;
                if (shapes.Count < Rules.MaxShapes) Spawn(RandomType(), player, bodies);
            }
            return events;
        }

        public int PointsFor(ShapeType type) => Target == null || type == Target ? Rules.Points : -Rules.Penalty;

        /// <summary>The person shapes appear around: whoever's nearest the middle of the Kinect's view.</summary>
        public static Body Player(IReadOnlyList<Body> bodies)
        {
            Body best = null;
            var bestOff = float.MaxValue;
            foreach (var body in bodies)
            {
                var middle = body.Middle;
                if (middle == null) continue;
                var off = Math.Abs(middle.Value.X / Math.Max(0.5f, middle.Value.Z));
                if (off >= bestOff) continue;
                bestOff = off;
                best = body;
            }
            return best;
        }

        private bool Touched(Vector3 position, IReadOnlyList<Body> bodies)
        {
            foreach (var body in bodies)
            {
                foreach (var joint in Body.Catchers)
                {
                    var at = body[joint];
                    if (at != null && body.Sees(joint) && Vector3.Distance(at.Value, position) < Rules.CatchRadius) return true;
                }
            }
            return false;
        }

        /// <summary>Puts a shape somewhere within the player's reach, clear of hands, feet and other shapes, if a spot is found.</summary>
        private void Spawn(ShapeType type, Body player, IReadOnlyList<Body> bodies)
        {
            var middle = player.Middle.Value;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                // Camera space has X to the Kinect's left, Y up and Z away from it; "in front of"
                // the player is toward the Kinect.
                var spot = middle + new Vector3(
                    Between(-Rules.ReachSide, Rules.ReachSide),
                    Between(-Rules.ReachBelow, Rules.ReachAbove),
                    -Between(Rules.InFrontNear, Rules.InFrontFar));
                if (Near(spot, bodies) || shapes.Exists(s => Vector3.Distance(s.Position, spot) < Rules.SpawnClearance)) continue;
                var heading = (float)(random.NextDouble() * Math.PI * 2);
                var velocity = new Vector3((float)Math.Cos(heading), (float)Math.Sin(heading), 0f) * Rules.Drift;
                shapes.Add(new FloatingShape(nextId++, type, spot, velocity));
                return;
            }
        }

        private bool Near(Vector3 spot, IReadOnlyList<Body> bodies)
        {
            foreach (var body in bodies)
            {
                foreach (var joint in Body.Catchers)
                {
                    var at = body[joint];
                    if (at != null && Vector3.Distance(at.Value, spot) < Rules.SpawnClearance) return true;
                }
            }
            return false;
        }

        private float Between(float low, float high) => low + (float)random.NextDouble() * (high - low);

        private ShapeType RandomType()
        {
            if (Target != null && random.NextDouble() < Rules.TargetSpawnChance) return Target.Value;
            return ShapeTypes.All[random.Next(ShapeTypes.All.Length)];
        }

        /// <summary>Places a shape at an exact spot. For tests.</summary>
        internal void Place(ShapeType type, Vector3 position) => shapes.Add(new FloatingShape(nextId++, type, position, Vector3.Zero));
    }
}

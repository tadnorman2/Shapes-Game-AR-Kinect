using System;
using System.Collections.Generic;

namespace ShapesKinect.Core
{
    /// <summary>The collectable shapes. <see cref="Color"/> is 0xRRGGBB.</summary>
    public enum ShapeType { Cube, Triangle, Sphere, Star }

    public static class ShapeTypes
    {
        public static readonly ShapeType[] All = { ShapeType.Cube, ShapeType.Triangle, ShapeType.Sphere, ShapeType.Star };

        public static string Label(this ShapeType type) => type.ToString();

        public static string Symbol(this ShapeType type)
        {
            switch (type)
            {
                case ShapeType.Cube: return "■";
                case ShapeType.Triangle: return "▲";
                case ShapeType.Sphere: return "●";
                default: return "✦";
            }
        }

        public static int Color(this ShapeType type)
        {
            switch (type)
            {
                case ShapeType.Cube: return 0x3D7BFF;
                case ShapeType.Triangle: return 0xFFD233;
                case ShapeType.Sphere: return 0x4CE07A;
                default: return 0xF8EDC4;
            }
        }
    }

    /// <summary>The ground a round is played on, in table coordinates (meters, flat on the table).</summary>
    public interface IPlayArea
    {
        bool Contains(float x, float z);
    }

    /// <summary>A play area from a function, e.g. the table minus the objects on it.</summary>
    public sealed class PlayArea : IPlayArea
    {
        private readonly Func<float, float, bool> contains;
        public PlayArea(Func<float, float, bool> contains) => this.contains = contains;
        public bool Contains(float x, float z) => contains(x, z);
    }

    /// <summary>Tunable rules for a round. Distances are meters, times are seconds.</summary>
    public sealed class RoundRules
    {
        public float RoundSeconds = 180f;
        public float PlayerRadius = 0.03f;
        public float ShapeRadius = 0.025f;
        public float PlayerSpeed = 0.3f;
        public float SpawnInterval = 1.2f;
        public int MaxShapes = 12;
        /// <summary>Shapes appear within this distance of the player...</summary>
        public float SpawnRange = 1.0f;
        /// <summary>...but never closer than this, so they can't land on the player.</summary>
        public float MinSpawnDistance = 0.12f;
        /// <summary>Opening stretch where every shape is worth points, before the first target is called.</summary>
        public float FreeSeconds = 15f;
        /// <summary>How long each called target lasts before a new one is picked.</summary>
        public float TargetSeconds = 20f;
        public int Points = 10;
        public int Penalty = 5;
        /// <summary>Chance that a new shape is the current target, so the target never runs dry.</summary>
        public float TargetSpawnChance = 0.35f;
    }

    public sealed class Collectable
    {
        public int Id { get; }
        public ShapeType Type { get; }
        public float X { get; }
        public float Z { get; }
        /// <summary>How long the shape has been off the play area, e.g. under a real object.</summary>
        internal float OffAreaTime;

        public Collectable(int id, ShapeType type, float x, float z)
        {
            Id = id;
            Type = type;
            X = x;
            Z = z;
        }
    }

    public abstract class RoundEvent
    {
        public sealed class Collected : RoundEvent
        {
            public ShapeType Type { get; }
            public int Points { get; }
            public Collected(ShapeType type, int points) { Type = type; Points = points; }
        }

        public sealed class TargetChanged : RoundEvent
        {
            public ShapeType Target { get; }
            public TargetChanged(ShapeType target) { Target = target; }
        }

        public sealed class Finished : RoundEvent
        {
            public int Score { get; }
            public Finished(int score) { Score = score; }
        }
    }

    /// <summary>
    /// One timed round: roll the ball into shapes for points. For the first stretch any shape
    /// scores; after that the game calls out one shape at a time, and only that one scores, while
    /// any other costs points. The player starts at (<paramref name="startX"/>, <paramref name="startZ"/>).
    /// </summary>
    public sealed class GameRound
    {
        public RoundRules Rules { get; }
        private readonly IPlayArea area;
        private readonly Random random;
        private readonly List<Collectable> shapes = new List<Collectable>();
        private int nextId;
        private float spawnTimer;
        private float nextTargetAt;

        public float PlayerX { get; private set; }
        public float PlayerZ { get; private set; }
        public int Score { get; private set; }
        public float Elapsed { get; private set; }

        /// <summary>The only shape worth points right now, or null while any shape counts.</summary>
        public ShapeType? Target { get; private set; }

        public IReadOnlyList<Collectable> Shapes => shapes;
        public float TimeLeft => Math.Max(Rules.RoundSeconds - Elapsed, 0f);
        public bool IsFinished => Elapsed >= Rules.RoundSeconds;

        public GameRound(IPlayArea area, RoundRules rules = null, Random random = null, float startX = 0f, float startZ = 0f)
        {
            this.area = area;
            Rules = rules ?? new RoundRules();
            this.random = random ?? new Random();
            PlayerX = startX;
            PlayerZ = startZ;
            nextTargetAt = Rules.FreeSeconds;
            for (var i = 0; i < Rules.MaxShapes / 2; i++) Spawn(RandomType());
        }

        /// <summary>
        /// Advances the round by <paramref name="dt"/> seconds. (<paramref name="moveX"/>,
        /// <paramref name="moveZ"/>) is the direction to move in table axes; a length of 1 is full speed.
        /// </summary>
        public List<RoundEvent> Update(float dt, float moveX, float moveZ)
        {
            var events = new List<RoundEvent>();
            if (IsFinished) return events;
            Elapsed += dt;

            Move(moveX, moveZ, dt);
            RemoveStranded(dt);

            var reach = Rules.PlayerRadius + Rules.ShapeRadius;
            for (var i = shapes.Count - 1; i >= 0; i--)
            {
                var shape = shapes[i];
                if (Distance(shape.X - PlayerX, shape.Z - PlayerZ) >= reach) continue;
                shapes.RemoveAt(i);
                var points = PointsFor(shape.Type);
                Score = Math.Max(Score + points, 0);
                events.Add(new RoundEvent.Collected(shape.Type, points));
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
                var newTarget = choices[random.Next(choices.Count)];
                Target = newTarget;
                nextTargetAt += Rules.TargetSeconds;
                StockTarget(newTarget);
                events.Add(new RoundEvent.TargetChanged(newTarget));
            }

            spawnTimer += dt;
            while (spawnTimer >= Rules.SpawnInterval)
            {
                spawnTimer -= Rules.SpawnInterval;
                if (shapes.Count < Rules.MaxShapes) Spawn(RandomType());
            }
            // While the play area has little room, spawns often fail; keep trying every update so
            // there's always something to chase.
            if (shapes.Count < MinShapes) Spawn(RandomType());
            return events;
        }

        public int PointsFor(ShapeType type) => Target == null || type == Target ? Rules.Points : -Rules.Penalty;

        /// <summary>Moves the player, keeping the whole ball on the play area; blocked moves slide along the edge.</summary>
        private void Move(float moveX, float moveZ, float dt)
        {
            var length = Distance(moveX, moveZ);
            if (length == 0f) return;
            var speed = length > 1f ? Rules.PlayerSpeed / length : Rules.PlayerSpeed;
            var dx = moveX * speed * dt;
            var dz = moveZ * speed * dt;
            // If the ground shifted out from under the player, let them move freely to get back.
            if (!Fits(PlayerX, PlayerZ))
            {
                PlayerX += dx;
                PlayerZ += dz;
            }
            else if (Fits(PlayerX + dx, PlayerZ + dz))
            {
                PlayerX += dx;
                PlayerZ += dz;
            }
            else if (Fits(PlayerX + dx, PlayerZ))
            {
                PlayerX += dx;
            }
            else if (Fits(PlayerX, PlayerZ + dz))
            {
                PlayerZ += dz;
            }
        }

        private bool Fits(float x, float z)
        {
            var r = Rules.PlayerRadius;
            return area.Contains(x, z) && area.Contains(x + r, z) && area.Contains(x - r, z) &&
                area.Contains(x, z + r) && area.Contains(x, z - r);
        }

        /// <summary>Drops shapes that have been off the play area for a moment, e.g. under a real object.</summary>
        private void RemoveStranded(float dt)
        {
            for (var i = shapes.Count - 1; i >= 0; i--)
            {
                var shape = shapes[i];
                if (area.Contains(shape.X, shape.Z))
                {
                    shape.OffAreaTime = 0f;
                    continue;
                }
                shape.OffAreaTime += dt;
                if (shape.OffAreaTime > StrandedSeconds) shapes.RemoveAt(i);
            }
        }

        /// <summary>Makes sure a freshly called target has at least two shapes out.</summary>
        private void StockTarget(ShapeType type)
        {
            var have = 0;
            foreach (var s in shapes) if (s.Type == type) have++;
            for (var i = have; i < 2; i++)
            {
                if (shapes.Count >= Rules.MaxShapes)
                {
                    var other = shapes.FindIndex(s => s.Type != type);
                    if (other >= 0) shapes.RemoveAt(other);
                }
                Spawn(type);
            }
        }

        private ShapeType RandomType()
        {
            if (Target != null && random.NextDouble() < Rules.TargetSpawnChance) return Target.Value;
            return ShapeTypes.All[random.Next(ShapeTypes.All.Length)];
        }

        /// <summary>Drops a shape at a random open spot on the play area near the player, if one is found.</summary>
        private void Spawn(ShapeType type)
        {
            for (var attempt = 0; attempt < SpawnAttempts; attempt++)
            {
                var angle = random.NextDouble() * 2 * Math.PI;
                var min = Rules.MinSpawnDistance;
                var max = Rules.SpawnRange;
                // Uniform over a ring around the player.
                var distance = Math.Sqrt(min * min + random.NextDouble() * (max * max - min * min));
                var x = PlayerX + (float)(Math.Cos(angle) * distance);
                var z = PlayerZ + (float)(Math.Sin(angle) * distance);
                var clear = true;
                foreach (var s in shapes)
                {
                    if (Distance(s.X - x, s.Z - z) < Rules.ShapeRadius * 3) { clear = false; break; }
                }
                if (clear && area.Contains(x, z))
                {
                    shapes.Add(new Collectable(nextId++, type, x, z));
                    return;
                }
            }
        }

        /// <summary>Places a shape at an exact spot. For tests.</summary>
        internal void Place(ShapeType type, float x, float z) => shapes.Add(new Collectable(nextId++, type, x, z));

        private static float Distance(float dx, float dz) => (float)Math.Sqrt(dx * dx + dz * dz);

        private const int SpawnAttempts = 30;
        private const int MinShapes = 3;
        private const float StrandedSeconds = 1f;
    }
}

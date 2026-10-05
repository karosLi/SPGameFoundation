using SPF.Contracts;
using SPF.L1.Body;
using SPF.L1.Spatial;
using SPF.L2.Buffs;
using SPF.L2.Elements;
using SPF.Runtime.World;
using Unity.Mathematics;

namespace SnakeFoundation
{
    /// <summary>Per-region element populations (food and props per chunk).</summary>
    public sealed class RegionPopulations : IResettableResource
    {
        public RegionPopulations(SnakeRuntimeConfig config)
        {
            int n = config.Regions.Length;
            Food = new ChunkPopulation[n];
            Props = new ChunkPopulation[n];
            for (int i = 0; i < n; i++)
            {
                var r = config.Regions[i];
                var layout = new ChunkLayout(r.Min, r.Size, config.Capacity.ChunkSize);
                Food[i] = new ChunkPopulation(layout, (int)math.round(config.Settings.FoodPerChunk * r.FoodDensity));
                Props[i] = new ChunkPopulation(layout, config.Settings.PropsPerChunk);
            }
        }

        public ChunkPopulation[] Food { get; }
        public ChunkPopulation[] Props { get; }

        public void OnReset()
        {
            foreach (var p in Food) p.OnReset();
            foreach (var p in Props) p.OnReset();
        }
    }

    /// <summary>Main-thread creation and removal of snakes and items (ApplyCommands phase only).</summary>
    public static class SnakeSpawner
    {
        public static EntityHandle SpawnSnake(SimWorld world, SnakeRuntimeConfig config, SnakeGameState game,
            int region, float2 position, float2 heading, float mass, SnakeFlags flags, int skin, ref Unity.Mathematics.Random random)
        {
            var s = config.Settings;
            var bodies = world.Resource(SnakeKeys.Bodies);
            float length = s.Growth.Length(mass);
            float spacing = s.TrailSpacingFor(s.Growth.Radius(mass));
            int points = TrailMath.PointsForLength(length, spacing);
            if (!bodies.TryAllocate(math.min(points + 16, s.MaxTrailPoints), out var trail))
                return EntityHandle.Null;

            var handle = world.CreateEntity(SnakeKeys.Snake, out int row);
            if (handle.IsNull)
            {
                bodies.Free(ref trail);
                return handle;
            }

            heading = math.normalizesafe(heading, new float2(1f, 0f));
            TrailMath.Reset(ref trail, bodies.Points, position, -heading, spacing, math.min(points, trail.Capacity));

            var table = world.Table(SnakeKeys.Snake);
            table.Column(SnakeKeys.Head).Set(row, position);
            table.Column(SnakeKeys.PrevHead).Set(row, position);
            table.Column(SnakeKeys.Heading).Set(row, heading);
            table.Column(SnakeKeys.Speed).Set(row, s.BaseSpeed);
            table.Column(SnakeKeys.Mass).Set(row, mass);
            float radius = s.Growth.Radius(mass);
            table.Column(SnakeKeys.Radius).Set(row, radius);
            table.Column(SnakeKeys.Length).Set(row, length);
            table.Column(SnakeKeys.Trail).Set(row, trail);
            table.Column(SnakeKeys.PrevArc).Set(row, 0f);
            table.Column(SnakeKeys.Bounds).Set(row, new float4(position - length - radius, position + length + radius));
            table.Column(SnakeKeys.Info).Set(row, new SnakeInfo
            {
                Id = game.NextSnakeId++,
                Region = (byte)region,
                Flags = flags,
                Skin = (ushort)math.max(skin, 0),
                Protection = s.SpawnProtection,
            });
            table.Column(SnakeKeys.Control).Set(row, new SnakeControl { TargetDirection = heading });
            table.Column(SnakeKeys.AI).Set(row, new AIState
            {
                Intent = AIIntent.Wander,
                Target = position + heading * 20f,
                TargetRow = -1,
                Aggression = random.NextFloat(0.1f, 1f),
                Greed = random.NextFloat(0.3f, 1f),
                Caution = random.NextFloat(0.2f, 1f),
            });
            table.Column(SnakeKeys.Stats).Set(row, EffectiveStats.Neutral);
            return handle;
        }

        public static void DestroySnake(SimWorld world, EntityHandle handle)
        {
            if (!world.Registry.TryResolve(handle, out _, out int row))
                return;
            var trails = world.Column(SnakeKeys.Trail);
            var trail = trails[row];
            world.Resource(SnakeKeys.Bodies).Free(ref trail);
            trails[row] = trail;
            world.DestroyEntity(handle);
        }

        /// <summary>Creates a food pellet if its chunk is instantiated, otherwise banks it in the chunk.</summary>
        public static bool SpawnFood(SimWorld world, ChunkPopulation population, in SnakeSettings s, float2 position, float value, uint color)
        {
            int chunk = population.Layout.IndexOf(position);
            if (!population.IsActive(chunk))
            {
                population.AddStored(chunk, 1);
                return false;
            }
            var handle = world.CreateEntity(SnakeKeys.Food, out int row);
            if (handle.IsNull)
                return false;
            world.Column(SnakeKeys.FoodPosition).Set(row, position);
            world.Column(SnakeKeys.FoodInfo).Set(row, new FoodInfo
            {
                Value = value,
                Radius = s.FoodRadius(value),
                Chunk = chunk,
                Color = color,
            });
            population.OnSpawned(chunk);
            return true;
        }

        public static bool SpawnProp(SimWorld world, SnakeRuntimeConfig config, ChunkPopulation population, float2 position, ref Unity.Mathematics.Random random)
        {
            if (config.PropCount == 0)
                return false;
            int chunk = population.Layout.IndexOf(position);
            var handle = world.CreateEntity(SnakeKeys.Prop, out int row);
            if (handle.IsNull)
                return false;
            float pick = random.NextFloat(0f, config.PropWeightTotal);
            int kind = 0;
            for (; kind < config.PropCount - 1; kind++)
            {
                pick -= config.Props[kind].SpawnWeight;
                if (pick <= 0f) break;
            }
            world.Column(SnakeKeys.PropPosition).Set(row, position);
            world.Column(SnakeKeys.PropInfo).Set(row, new PropInfo { Kind = (byte)(kind + 1), Radius = config.Props[kind].Radius, Chunk = chunk });
            population.OnSpawned(chunk);
            return true;
        }

        public static uint RandomFoodColor(ref Unity.Mathematics.Random random) => random.NextUInt(0, 8);

        /// <summary>Random point inside a chunk (clipped to the region).</summary>
        public static float2 RandomPointInChunk(in ChunkLayout layout, int chunk, in RegionDef region, ref Unity.Mathematics.Random random)
        {
            float2 min = math.max(layout.MinOf(layout.CoordOfIndex(chunk)), region.Min + 1f);
            float2 max = math.min(min + layout.ChunkSize, region.Max - 1f);
            return random.NextFloat2(min, math.max(max, min + 0.01f));
        }
    }
}

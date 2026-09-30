using SPF.Contracts;
using SPF.Runtime.Scheduling;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace SnakeFoundation.Systems
{
    /// <summary>
    /// ApplyCommands: chunk bookkeeping for eaten items, food requested by jobs (boost / hit drops),
    /// replenishing chunks below their target, and spawning projectiles.
    /// </summary>
    sealed class ItemSpawnSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override int Order => 30;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var config = world.Resource(SnakeKeys.Config);
            var game = world.Resource(SnakeKeys.Game);
            var populations = world.Resource(SnakeKeys.Populations);
            var s = config.Settings;
            int region = game.ActiveRegion;
            var food = populations.Food[region];
            var props = populations.Props[region];
            var def = config.Regions[region];
            var random = SimRandom.Create(context.Seed, context.Time.Tick, 0xF00Du);

            var removed = world.Resource(SnakeKeys.RemovedItems);
            for (int i = 0; i < removed.Count; i++)
            {
                var item = removed[i];
                (item.x == 0 ? food : props).OnRemoved(item.y);
            }
            removed.Clear();

            // Requests come from parallel jobs; sort so spawn order (and therefore row order) is deterministic.
            var requests = world.Resource(SnakeKeys.FoodSpawns);
            var array = requests.AsArray();
            array.Sort(new FoodRequestOrder());
            for (int i = 0; i < array.Length; i++)
            {
                var r = array[i];
                if (math.all(r.Position >= def.Min) && math.all(r.Position <= def.Max))
                    SnakeSpawner.SpawnFood(world, food, s, r.Position, r.Value, r.Color);
            }
            requests.Clear();

            for (int n = 0; n < s.ReplenishChunksPerTick; n++)
            {
                int chunk = food.NextDeficit(4, s.ReplenishPerChunkPerTick, out int deficit);
                if (chunk < 0) break;
                for (int k = 0; k < deficit; k++)
                {
                    float2 p = SnakeSpawner.RandomPointInChunk(food.Layout, chunk, def, ref random);
                    if (!SnakeSpawner.SpawnFood(world, food, s, p, random.NextFloat(s.FoodValueMin, s.FoodValueMax), SnakeSpawner.RandomFoodColor(ref random)))
                        break;
                }
            }

            if ((context.Time.Tick & 7) == 0)
            {
                int chunk = props.NextDeficit(2, 1, out int deficit);
                if (chunk >= 0 && deficit > 0)
                    SnakeSpawner.SpawnProp(world, config, props, SnakeSpawner.RandomPointInChunk(props.Layout, chunk, def, ref random), ref random);
            }

            var projectileRequests = world.Resource(SnakeKeys.ProjectileSpawns);
            for (int i = 0; i < projectileRequests.Count; i++)
            {
                var r = projectileRequests[i];
                var handle = world.CreateEntity(SnakeKeys.Projectile, out int row);
                if (handle.IsNull) break;
                world.Column(SnakeKeys.ProjectilePosition).Set(row, r.Position);
                world.Column(SnakeKeys.ProjectileState).Set(row, new ProjectileState
                {
                    PreviousPosition = r.Position,
                    Velocity = r.Velocity,
                    Life = s.Skill.ProjectileLife,
                    Radius = s.Skill.ProjectileRadius,
                    OwnerId = r.OwnerId,
                    OwnerRow = r.OwnerRow,
                    Skill = r.Skill,
                });
            }
            projectileRequests.Clear();
            return dependency;
        }

        struct FoodRequestOrder : System.Collections.Generic.IComparer<FoodSpawnRequest>
        {
            public int Compare(FoodSpawnRequest a, FoodSpawnRequest b)
            {
                int c = a.Position.x.CompareTo(b.Position.x);
                if (c != 0) return c;
                c = a.Position.y.CompareTo(b.Position.y);
                return c != 0 ? c : a.Value.CompareTo(b.Value);
            }
        }
    }
}

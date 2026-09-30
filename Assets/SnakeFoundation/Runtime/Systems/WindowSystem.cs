using SPF.Contracts;
using SPF.L2.Elements;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Jobs;
using Unity.Mathematics;

namespace SnakeFoundation.Systems
{
    /// <summary>
    /// ApplyCommands: moves the active window (fine grids) with the player / camera focus, streams
    /// food and props in and out of chunks, and clears items when the active region changes.
    /// </summary>
    sealed class WindowSystem : SimSystemBase, IResettableSystem
    {
        int m_LastRegion = -1;
        int m_AttractTicks;

        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override int Order => 20;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var config = world.Resource(SnakeKeys.Config);
            var game = world.Resource(SnakeKeys.Game);
            var populations = world.Resource(SnakeKeys.Populations);
            var bodyGrid = world.Resource(SnakeKeys.BodyGrid);
            var itemGrid = world.Resource(SnakeKeys.ItemGrid);
            var headGrid = world.Resource(SnakeKeys.HeadGrid);
            int region = game.ActiveRegion;
            var def = config.Regions[region];

            if (region != m_LastRegion)
            {
                if (m_LastRegion >= 0)
                    ClearItems(world, populations, m_LastRegion);
                m_LastRegion = region;
                bodyGrid.Origin = new float2(float.MaxValue);   // force a re-centre
            }

            float2 focus = UpdateFocus(world, game);
            bodyGrid.Follow(focus, def.Min, def.Max);
            itemGrid.Origin = bodyGrid.Origin;
            headGrid.Origin = def.Min;

            float2 windowMin = math.max(bodyGrid.Origin, def.Min);
            float2 windowMax = math.min(bodyGrid.Origin + bodyGrid.Size, def.Max) - 0.001f;
            var random = SimRandom.Create(context.Seed, context.Time.Tick, 0x3149u);
            Stream(world, config, populations.Food[region], def, windowMin, windowMax, isFood: true, ref random);
            Stream(world, config, populations.Props[region], def, windowMin, windowMax, isFood: false, ref random);
            return dependency;
        }

        float2 UpdateFocus(SimWorld world, SnakeGameState game)
        {
            var heads = world.Column(SnakeKeys.Head);
            if (world.Registry.TryResolve(game.Player, out _, out int row))
            {
                game.Focus = heads[row];
                return game.Focus;
            }
            if (game.Flow == GameFlow.Attract && --m_AttractTicks <= 0)
            {
                // Follow the heaviest snake of the region so the menu shows some action.
                m_AttractTicks = 300;
                var infos = world.Column(SnakeKeys.Info);
                var masses = world.Column(SnakeKeys.Mass);
                int count = world.Table(SnakeKeys.Snake).Count;
                float best = -1f;
                for (int i = 0; i < count; i++)
                {
                    if (infos[i].Region != game.ActiveRegion || masses[i] <= best) continue;
                    best = masses[i];
                    game.Focus = heads[i];
                }
            }
            return game.Focus;
        }

        static void Stream(SimWorld world, SnakeRuntimeConfig config, ChunkPopulation population, in RegionDef region,
            float2 windowMin, float2 windowMax, bool isFood, ref Random random)
        {
            population.SetWindow(windowMin, windowMax);
            if (population.LeavingCount > 0)
            {
                var table = world.Table(isFood ? SnakeKeys.Food : SnakeKeys.Prop);
                var handles = table.Handles;
                var foodInfos = world.Column(SnakeKeys.FoodInfo);
                var propInfos = world.Column(SnakeKeys.PropInfo);
                // Backwards: swap-back removal only moves already-visited rows.
                for (int row = table.Count - 1; row >= 0; row--)
                {
                    int chunk = isFood ? foodInfos[row].Chunk : propInfos[row].Chunk;
                    if (population.IsActive(chunk)) continue;
                    population.Store(chunk);
                    world.DestroyEntity(handles[row]);
                }
            }

            var s = config.Settings;
            for (int i = 0; i < population.EnteringCount; i++)
            {
                int chunk = population.Entering(i);
                int count = population.TakeStored(chunk);
                for (int n = 0; n < count; n++)
                {
                    float2 p = SnakeSpawner.RandomPointInChunk(population.Layout, chunk, region, ref random);
                    bool ok = isFood
                        ? SnakeSpawner.SpawnFood(world, population, s, p, random.NextFloat(s.FoodValueMin, s.FoodValueMax), SnakeSpawner.RandomFoodColor(ref random))
                        : SnakeSpawner.SpawnProp(world, config, population, p, ref random);
                    if (!ok)
                    {
                        population.AddStored(chunk, count - n);
                        break;
                    }
                }
            }
        }

        static void ClearItems(SimWorld world, RegionPopulations populations, int region)
        {
            DestroyAll(world, SnakeKeys.Food);
            DestroyAll(world, SnakeKeys.Prop);
            DestroyAll(world, SnakeKeys.Projectile);
            populations.Food[region].DeactivateAll();
            populations.Props[region].DeactivateAll();
        }

        static void DestroyAll(SimWorld world, TableKey key)
        {
            var table = world.Table(key);
            var handles = table.Handles;
            for (int row = table.Count - 1; row >= 0; row--)
                world.DestroyEntity(handles[row]);
        }

        public void OnReset(SimWorld world)
        {
            m_LastRegion = -1;
            m_AttractTicks = 0;
        }
    }
}

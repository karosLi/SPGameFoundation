using SPF.Contracts;
using SPF.L1.Body;
using SPF.Runtime.Scheduling;
using SPF.Runtime.World;
using Unity.Jobs;
using Unity.Mathematics;

namespace SnakeFoundation.Systems
{
    /// <summary>
    /// ApplyCommands: turns last tick's deaths into dropped food and removed snakes, and handles
    /// the start request (spawning the player).
    /// </summary>
    sealed class LifecycleSystem : SimSystemBase
    {
        public override SimPhase Phase => SimPhase.ApplyCommands;
        public override int Order => 0;
        public override void Declare(AccessDeclaration access) { }

        public override JobHandle OnTick(in SimContext context, JobHandle dependency)
        {
            var world = context.World;
            var config = world.Resource(SnakeKeys.Config);
            var game = world.Resource(SnakeKeys.Game);
            var deaths = world.Resource(SnakeKeys.Deaths);
            var populations = world.Resource(SnakeKeys.Populations);
            var s = config.Settings;
            var random = SimRandom.Create(context.Seed, context.Time.Tick, 0xDEA7u);

            var table = world.Table(SnakeKeys.Snake);
            var heads = table.Column(SnakeKeys.Head);
            var trails = table.Column(SnakeKeys.Trail);
            var masses = table.Column(SnakeKeys.Mass);
            var infos = table.Column(SnakeKeys.Info);
            var radii = table.Column(SnakeKeys.Radius);
            var bodies = world.Resource(SnakeKeys.Bodies);

            for (int d = 0; d < deaths.Count; d++)
            {
                var death = deaths[d];
                if (!world.Registry.TryResolve(death.Victim, out _, out int row))
                    continue;
                var info = infos[row];
                DropBody(world, populations.Food[info.Region], s, bodies, trails[row], heads[row], masses[row], radii[row], ref random);

                if (death.Victim == game.Player)
                {
                    game.Flow = GameFlow.GameOver;
                    game.LastDeathCause = death.Cause;
                    game.LastKillerId = world.Registry.TryResolve(death.Killer, out _, out int killerRow) ? infos[killerRow].Id : 0;
                    game.Focus = heads[row];
                    game.Player = EntityHandle.Null;
                    game.Version++;
                }
                SnakeSpawner.DestroySnake(world, death.Victim);
            }
            deaths.Clear();

            if (game.StartRequested)
            {
                game.ConsumeStart();
                if (game.Flow != GameFlow.Playing || game.Player.IsNull)
                    SpawnPlayer(world, config, game, ref random);
            }
            return dependency;
        }

        static void DropBody(SimWorld world, SPF.L2.Elements.ChunkPopulation population, in SnakeSettings s, BodyStore bodies,
            TrailState trail, float2 head, float mass, float radius, ref Random random)
        {
            float length = TrailMath.BodyLength(trail, head, s.TrailSpacing);
            float totalValue = mass * s.DeathDropRatio / math.max(s.MassPerFoodValue, 1e-3f);
            int drops = math.clamp((int)(length / math.max(radius * 1.5f, 0.5f)), 1, s.MaxDropsPerDeath);
            float value = totalValue / drops;
            uint color = SnakeSpawner.RandomFoodColor(ref random);
            for (int i = 0; i < drops; i++)
            {
                float2 p = TrailMath.SampleBehind(trail, bodies.Points, head, length * i / drops, s.TrailSpacing);
                p += random.NextFloat2(-radius, radius) * 0.6f;
                SnakeSpawner.SpawnFood(world, population, s, p, value, color);
            }
        }

        internal static void SpawnPlayer(SimWorld world, SnakeRuntimeConfig config, SnakeGameState game, ref Random random)
        {
            var region = config.Regions[game.ActiveRegion];
            float2 center = region.Center;
            float2 position = center + random.NextFloat2(-50f, 50f);
            float2 heading = SPF.L1.Geometry.GeoMath.FromAngle(random.NextFloat(0f, 2f * math.PI));
            var handle = SnakeSpawner.SpawnSnake(world, config, game, game.ActiveRegion, position, heading,
                config.Settings.StartMass, SnakeFlags.Player, game.PlayerSkin, ref random);
            if (handle.IsNull)
                return;
            game.Player = handle;
            game.Flow = GameFlow.Playing;
            game.SurvivalSeconds = 0f;
            game.BestMass = config.Settings.StartMass;
            game.PlayerKills = 0;
            game.Command = default;
            game.Version++;
        }
    }
}
